using System.Windows.Controls;

namespace PosSystem.Main.Templates
{
    public partial class MoveTableTemplate : UserControl
    {
        public MoveTableTemplate()
        {
            InitializeComponent();
        }

        public void SetData(string oldTableName, string newTableName)
        {
            txtMoveInfo.Text = $"{oldTableName} chuyển đến {newTableName}";
            txtTime.Text = $"{System.DateTime.Now:HH:mm:ss}";
        }
    }
}

