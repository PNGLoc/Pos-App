using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PosSystem.Main.Helpers
{
    public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
    {
        public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
            nameof(ItemWidth),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
            nameof(ItemHeight),
            typeof(double),
            typeof(VirtualizingWrapPanel),
            new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double ItemWidth
        {
            get => (double)GetValue(ItemWidthProperty);
            set => SetValue(ItemWidthProperty, value);
        }

        public double ItemHeight
        {
            get => (double)GetValue(ItemHeightProperty);
            set => SetValue(ItemHeightProperty, value);
        }

        private Size _extent = new(0, 0);
        private Size _viewport = new(0, 0);
        private Point _offset;

        private int _itemsPerRow = 1;
        private int _firstIndex;
        private int _lastIndex;

        private ItemsControl? _itemsControl;

        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
        {
            base.OnItemsChanged(sender, args);

            switch (args.Action)
            {
                case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                    _offset = new Point(0, 0);
                    break;
            }

            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _itemsControl = ItemsControl.GetItemsOwner(this);
            if (_itemsControl == null)
                return availableSize;

            var itemCount = _itemsControl.HasItems ? _itemsControl.Items.Count : 0;

            var viewportWidth = double.IsInfinity(availableSize.Width) ? _viewport.Width : availableSize.Width;
            var viewportHeight = double.IsInfinity(availableSize.Height) ? _viewport.Height : availableSize.Height;

            if (double.IsNaN(viewportWidth) || viewportWidth <= 0)
                viewportWidth = 0;
            if (double.IsNaN(viewportHeight) || viewportHeight <= 0)
                viewportHeight = 0;

            _itemsPerRow = Math.Max(1, (int)Math.Floor(viewportWidth / Math.Max(1, ItemWidth)));

            var totalRows = _itemsPerRow == 0 ? 0 : (int)Math.Ceiling((double)itemCount / _itemsPerRow);

            _viewport = new Size(viewportWidth, viewportHeight);
            _extent = new Size(_itemsPerRow * ItemWidth, totalRows * ItemHeight);

            ScrollOwner?.InvalidateScrollInfo();

            if (itemCount == 0)
            {
                CleanUpItems(0, -1);
                return availableSize;
            }

            var firstVisibleRow = (int)Math.Floor(VerticalOffset / Math.Max(1, ItemHeight));
            var visibleRowCount = (int)Math.Ceiling((_viewport.Height + (VerticalOffset % Math.Max(1, ItemHeight))) / Math.Max(1, ItemHeight));
            visibleRowCount = Math.Max(1, visibleRowCount);

            var firstIndex = firstVisibleRow * _itemsPerRow;
            var lastIndex = Math.Min(itemCount - 1, ((firstVisibleRow + visibleRowCount + 1) * _itemsPerRow) - 1);

            if (firstIndex < 0) firstIndex = 0;
            if (lastIndex < firstIndex) lastIndex = firstIndex;

            _firstIndex = firstIndex;
            _lastIndex = lastIndex;

            RealizeItems(firstIndex, lastIndex);
            CleanUpItems(firstIndex, lastIndex);

            foreach (UIElement child in InternalChildren)
                child.Measure(new Size(ItemWidth, ItemHeight));

            return availableSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var leftPad = 0d;
            var usedWidth = _itemsPerRow * ItemWidth;
            if (!double.IsNaN(finalSize.Width) && finalSize.Width > usedWidth)
                leftPad = (finalSize.Width - usedWidth) / 2d;

            foreach (UIElement child in InternalChildren)
            {
                var itemIndex = _itemsControl?.ItemContainerGenerator.IndexFromContainer(child) ?? -1;
                if (itemIndex < 0)
                    continue;

                var row = itemIndex / _itemsPerRow;
                var column = itemIndex % _itemsPerRow;

                var x = leftPad + (column * ItemWidth);
                var y = (row * ItemHeight) - VerticalOffset;

                child.Arrange(new Rect(new Point(x, y), new Size(ItemWidth, ItemHeight)));
            }

            return finalSize;
        }

        private void RealizeItems(int startIndex, int endIndex)
        {
            if (ItemContainerGenerator is not IItemContainerGenerator generator)
                return;

            var startPos = generator.GeneratorPositionFromIndex(startIndex);
            var childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;

            using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
            {
                for (var itemIndex = startIndex; itemIndex <= endIndex; itemIndex++, childIndex++)
                {
                    var newlyRealized = false;
                    var child = generator.GenerateNext(out newlyRealized) as UIElement;
                    if (child == null)
                        continue;

                    if (newlyRealized)
                    {
                        if (childIndex >= InternalChildren.Count)
                            AddInternalChild(child);
                        else
                            InsertInternalChild(childIndex, child);

                        generator.PrepareItemContainer(child);
                    }
                }
            }
        }

        private void CleanUpItems(int startIndex, int endIndex)
        {
            if (ItemContainerGenerator is not IItemContainerGenerator generator)
                return;

            for (var i = InternalChildren.Count - 1; i >= 0; i--)
            {
                var child = InternalChildren[i];
                var itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));

                if (itemIndex < startIndex || itemIndex > endIndex)
                {
                    generator.Remove(new GeneratorPosition(i, 0), 1);
                    RemoveInternalChildRange(i, 1);
                }
            }
        }

        public bool CanVerticallyScroll { get; set; } = true;
        public bool CanHorizontallyScroll { get; set; }

        public double ExtentWidth => _extent.Width;
        public double ExtentHeight => _extent.Height;
        public double ViewportWidth => _viewport.Width;
        public double ViewportHeight => _viewport.Height;
        public double HorizontalOffset => _offset.X;
        public double VerticalOffset => _offset.Y;

        public ScrollViewer? ScrollOwner { get; set; }

        public void LineDown() => SetVerticalOffset(VerticalOffset + ItemHeight);
        public void LineUp() => SetVerticalOffset(VerticalOffset - ItemHeight);
        public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
        public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
        public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + (ItemHeight * 1));
        public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - (ItemHeight * 1));

        public void LineLeft() { }
        public void LineRight() { }
        public void PageLeft() { }
        public void PageRight() { }
        public void MouseWheelLeft() { }
        public void MouseWheelRight() { }

        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            if (visual is not UIElement element)
                return rectangle;

            var itemIndex = _itemsControl?.ItemContainerGenerator.IndexFromContainer(element) ?? -1;
            if (itemIndex < 0)
                return rectangle;

            var row = itemIndex / _itemsPerRow;
            var y = row * ItemHeight;

            if (y < VerticalOffset)
                SetVerticalOffset(y);
            else if (y + ItemHeight > VerticalOffset + ViewportHeight)
                SetVerticalOffset((y + ItemHeight) - ViewportHeight);

            rectangle.Y = y;
            return rectangle;
        }

        public void SetHorizontalOffset(double offset) { }

        public void SetVerticalOffset(double offset)
        {
            if (!CanVerticallyScroll)
                return;

            var newOffset = offset;
            if (newOffset < 0)
                newOffset = 0;
            if (newOffset > ExtentHeight - ViewportHeight)
                newOffset = Math.Max(0, ExtentHeight - ViewportHeight);

            if (Math.Abs(newOffset - _offset.Y) < 0.5)
                return;

            _offset.Y = newOffset;
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
        }
    }
}
