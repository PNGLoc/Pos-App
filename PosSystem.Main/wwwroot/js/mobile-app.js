// --- PHIÊN BẢN V7: REAL-TIME FULL DUPLEX & CONCURRENCY CHECK ---
const API_URL = '/api';

// CẤU HÌNH TÊN HIỂN THỊ
const TABLE_TYPE_NAMES = {
    'DineIn': 'Tại quán',
    'TakeAway': 'Mang về',
    'Pickup': 'Khách lấy',
    'Delivery': 'Ship'
};

let currentUser = null;
let appState = {
    tables: [],
    categories: [],
    currentTableId: null,
    orderDetails: [], // Chứa toàn bộ món từ server (cả New và Sent)
    tempMenuSelection: {},
    currentFilter: 'All'
};

// --- KHỞI TẠO ---
document.addEventListener('DOMContentLoaded', async () => {
    const userStr = localStorage.getItem('posUser');
    if (!userStr) { window.location.href = 'index.html'; return; }
    currentUser = JSON.parse(userStr);

    await loadTables();
    await loadMenuData();
    setTimeout(() => initSignalR(), 500);
});

// --- SIGNALR (XỬ LÝ ĐỒNG BỘ) ---
function initSignalR() {
    if (typeof signalR === 'undefined') return;
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/posHub")
        .withAutomaticReconnect()
        .build();

    connection.on("TableUpdated", (tableId) => {
        loadTables(false);
        // Nếu đang xem đúng bàn đó -> Tải lại order ngay
        if (appState.currentTableId == tableId) {
            loadOrderData(tableId);
        }
    });

    connection.start().catch(err => console.error("SignalR Error:", err));
}

// --- LOGIC CHÍNH ---

async function loadTables(shouldRenderFilters = true) {
    try {
        const res = await fetch(`${API_URL}/Table`);
        if (!res.ok) throw new Error("Lỗi tải bàn");
        appState.tables = await res.json();

        // Kiểm tra xem bàn mình đang thao tác có bị ai đó đóng (thanh toán) rồi không?
        if (appState.currentTableId) {
            const currentTable = appState.tables.find(t => t.tableID === appState.currentTableId);
            if (currentTable && currentTable.tableStatus === 'Empty' && document.getElementById('view-detail').classList.contains('active')) {
                // Nếu bàn đã thành Empty mà mình vẫn đang ở trang chi tiết -> Có ai đó đã thanh toán
                alert("Bàn này đã được thanh toán hoặc đóng bởi thiết bị khác!");
                showView('view-tables'); // Đá về trang chủ
                appState.currentTableId = null;
            }
        }

        if (shouldRenderFilters) renderFilterButtons();
        renderTables(appState.currentFilter);
    } catch (e) { console.error(e); }
}

function renderTables(filterType) {
    const grid = document.getElementById('tableGrid');
    if (!grid) return;
    grid.innerHTML = '';

    appState.currentFilter = filterType;
    // Update UI buttons active state
    document.querySelectorAll('#tableFilters .filter-btn').forEach(b => {
        b.classList.remove('active');
        // Logic so sánh text đơn giản để highlight
        if ((filterType === 'All' && b.innerText === 'Tất cả') ||
            (TABLE_TYPE_NAMES[filterType] === b.innerText)) {
            b.classList.add('active');
        }
    });

    const filtered = filterType === 'All' ? appState.tables : appState.tables.filter(t => t.tableType === filterType);

    if (filtered.length === 0) {
        grid.innerHTML = '<div class="text-muted text-center w-100 mt-5">Không có bàn nào.</div>';
        return;
    }

    filtered.forEach(t => {
        const div = document.createElement('div');
        const isOccupied = t.tableStatus === 'Occupied';
        let icon = 'fa-chair';
        if (t.tableType === 'Delivery') icon = 'fa-motorcycle';
        if (t.tableType === 'TakeAway') icon = 'fa-shopping-bag';

        div.className = `table-card ${isOccupied ? 'occupied' : ''}`;
        div.onclick = () => openTableDetail(t);
        div.innerHTML = `
            <div class="fs-4 mb-1"><i class="fas ${icon}"></i></div>
            <div class="fw-bold">${t.tableName || "Bàn"}</div>
            <small class="${isOccupied ? 'text-danger' : 'text-success'}">${isOccupied ? 'Có khách' : 'Trống'}</small>
        `;
        grid.appendChild(div);
    });
}
function filterTables(type) { renderTables(type); }

function renderFilterButtons() {
    const filterContainer = document.getElementById('tableFilters');
    if (!filterContainer) return;
    filterContainer.innerHTML = '';

    const btnAll = document.createElement('button');
    btnAll.className = `filter-btn ${appState.currentFilter === 'All' ? 'active' : ''}`;
    btnAll.innerText = 'Tất cả';
    btnAll.onclick = () => filterTables('All');
    filterContainer.appendChild(btnAll);

    const uniqueTypes = [...new Set(appState.tables.map(t => t.tableType))];
    uniqueTypes.forEach(type => {
        if (!type) return;
        const btn = document.createElement('button');
        btn.className = `filter-btn`;
        btn.innerText = TABLE_TYPE_NAMES[type] || type;
        btn.onclick = () => filterTables(type);
        filterContainer.appendChild(btn);
    });
}

// --- LOGIC CHI TIẾT BÀN ---
function openTableDetail(table) {
    appState.currentTableId = table.tableID;
    document.getElementById('detailTableName').innerText = table.tableName;
    document.getElementById('detailTableStatus').innerText = table.tableStatus;

    loadOrderData(table.tableID); // Tải dữ liệu từ server
    showView('view-detail');
}

// --- GỬI BẾP (CÓ CHECK TRẠNG THÁI ĐỂ TRÁNH BUG) ---
// --- GỬI BẾP (GỌI API IN & ĐỔI STATUS) ---
async function sendOrderToKitchen() {
    const btn = document.querySelector('#cartActionBar button');
    btn.disabled = true;
    btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Đang gửi...';

    try {
        const res = await fetch(`${API_URL}/Order/${appState.currentTableId}/send`, { method: 'POST' });

        if (res.ok) {
            showToast('Đã gửi lệnh in bếp!');
            // Chuyển sang tab Đã xác nhận
            const tabEl = document.querySelector('a[href="#tab-confirmed"]');
            if (tabEl) new bootstrap.Tab(tabEl).show();

            // Reload (SignalR sẽ làm, nhưng gọi luôn cho mượt)
            loadOrderData(appState.currentTableId);
        } else {
            showToast('Không có món mới để gửi', 'warning');
        }
    } catch (e) {
        showToast('Lỗi kết nối!', 'danger');
    } finally {
        btn.disabled = false;
        btn.innerHTML = '<i class="fas fa-paper-plane"></i> Gửi thực đơn';
    }
}

// --- CÁC HÀM UI KHÁC GIỮ NGUYÊN NHƯ CŨ ---
function showView(viewId) {
    document.querySelectorAll('.view-section').forEach(el => el.classList.remove('active'));
    document.getElementById(viewId).classList.add('active');
}
function showToast(msg, type = 'success') {
    const toastEl = document.getElementById('liveToast');
    if (toastEl) {
        document.getElementById('toastMessage').innerText = msg;
        toastEl.className = `toast align-items-center text-white bg-${type} border-0`;
        new bootstrap.Toast(toastEl).show();
    }
}
// HÀM QUAN TRỌNG: Tải order và chia vào 2 tab
async function loadOrderData(tableId) {
    try {
        const res = await fetch(`${API_URL}/Order/${tableId}`);
        if (res.ok) {
            const data = await res.json();
            appState.orderDetails = data.details || [];

            // Tính tổng tiền đã chốt (món không phải New)
            const confirmedTotal = appState.orderDetails
                .filter(d => d.itemStatus !== 'New')
                .reduce((sum, d) => sum + d.totalAmount, 0);
            document.getElementById('confirmedTotal').innerText = confirmedTotal.toLocaleString() + 'đ';
        } else {
            appState.orderDetails = []; // Bàn trống
            document.getElementById('confirmedTotal').innerText = '0đ';
        }
    } catch (e) {
        appState.orderDetails = [];
    }

    // Vẽ lại giao diện 2 tab
    renderCartTab();      // Tab 1: Món New
    renderConfirmedTab(); // Tab 2: Món Sent/Done
}
// --- TAB 1: CART (HIỂN THỊ MÓN STATUS = 'New') ---
function renderCartTab() {
    const container = document.getElementById('cartList');
    const actionBar = document.getElementById('cartActionBar');
    container.innerHTML = '';

    // Lọc các món MỚI (chưa gửi bếp)
    const cartItems = appState.orderDetails.filter(d => d.itemStatus === 'New');

    if (cartItems.length === 0) {
        container.innerHTML = `<div class="text-center text-muted mt-5"><i class="fas fa-shopping-basket fs-1 mb-3"></i><br>Giỏ hàng trống</div>`;
        actionBar.classList.add('d-none');
        return;
    }

    let total = 0;
    cartItems.forEach((item) => {
        total += item.totalAmount;
        const div = document.createElement('div');
        div.className = 'd-flex justify-content-between align-items-center p-3 border-bottom bg-white';
        div.innerHTML = `
            <div>
                <div class="fw-bold">${item.dishName}</div>
                <div class="text-muted small">${item.unitPrice.toLocaleString()}đ x ${item.quantity}</div>
                <div class="text-warning small fst-italic">"${item.note || "Chờ gửi bếp"}"</div>
            </div>
            <div class="fw-bold">${item.totalAmount.toLocaleString()}đ</div>
        `;
        container.appendChild(div);
    });

    document.getElementById('cartTotalMoney').innerText = total.toLocaleString() + 'đ';
    actionBar.classList.remove('d-none');
    actionBar.style.display = 'flex';
}

// --- TAB 2: CONFIRMED (HIỂN THỊ MÓN STATUS != 'New') ---
function renderConfirmedTab() {
    const container = document.getElementById('confirmedList');
    container.innerHTML = '';

    const items = appState.orderDetails.filter(d => d.itemStatus !== 'New');

    if (items.length === 0) {
        container.innerHTML = `<div class="text-center text-muted mt-5">Chưa có món nào được gọi</div>`;
        return;
    }

    // Gộp món hiển thị cho gọn
    const grouped = [];
    items.forEach(item => {
        const key = `${item.dishID}_${(item.note || "").trim()}_${item.itemStatus}`;
        const exist = grouped.find(g => `${g.dishID}_${(g.note || "").trim()}_${g.itemStatus}` === key);
        if (exist) { exist.quantity += item.quantity; exist.totalAmount += item.totalAmount; }
        else grouped.push({ ...item });
    });

    grouped.forEach(d => {
        let badge = 'bg-secondary', txt = d.itemStatus;
        if (d.itemStatus === 'Sent') { badge = 'bg-info text-dark'; txt = 'Đã gửi'; }
        else if (d.itemStatus === 'Done') { badge = 'bg-success'; txt = 'Đã ra'; }

        const div = document.createElement('div');
        div.className = 'd-flex justify-content-between p-2 border-bottom';
        div.innerHTML = `
            <div>
                <span class="fw-bold">${d.dishName}</span> <br>
                <small class="text-muted">${d.quantity} x ${(d.totalAmount / d.quantity).toLocaleString()}</small>
                ${d.note ? `<br><small class="text-warning fst-italic">"${d.note}"</small>` : ''}
            </div>
            <div class="text-end">
                <div class="fw-bold">${d.totalAmount.toLocaleString()}</div>
                <span class="badge ${badge}">${txt}</span>
            </div>
        `;
        container.appendChild(div);
    });
}
function updateCartItem(index, delta) {
    appState.cart[index].quantity += delta;
    if (appState.cart[index].quantity <= 0) appState.cart.splice(index, 1);
    renderCartTab();
}
function removeCartItem(index) { appState.cart.splice(index, 1); renderCartTab(); }

async function loadConfirmedOrders(tableId) {
    const container = document.getElementById('confirmedList');
    container.innerHTML = '<div class="text-center mt-3"><div class="spinner-border text-primary"></div></div>';
    try {
        const res = await fetch(`${API_URL}/Order/${tableId}`);
        if (!res.ok) {
            container.innerHTML = `<div class="text-center text-muted mt-5">Chưa có món nào</div>`;
            document.getElementById('confirmedTotal').innerText = '0đ';
            return;
        }
        const data = await res.json();
        document.getElementById('confirmedTotal').innerText = (data.finalAmount || 0).toLocaleString() + 'đ';
        container.innerHTML = '';
        if (data.details && data.details.length > 0) {
            const grouped = [];
            data.details.forEach(item => {
                const note = (item.note || "").trim();
                const exist = grouped.find(g => g.dishID === item.dishID && (g.note || "").trim() === note);
                if (exist) { exist.quantity += item.quantity; exist.totalAmount += item.totalAmount; }
                else grouped.push({ ...item });
            });
            grouped.forEach(d => {
                let badge = 'bg-secondary', txt = d.itemStatus;
                if (d.itemStatus === 'New') { badge = 'bg-primary'; txt = 'Mới'; }
                else if (d.itemStatus === 'Sent') { badge = 'bg-info text-dark'; txt = 'Đã gửi'; }
                else if (d.itemStatus === 'Done') { badge = 'bg-success'; txt = 'Đã ra'; }
                const div = document.createElement('div');
                div.className = 'd-flex justify-content-between p-2 border-bottom';
                div.innerHTML = `<div><span class="fw-bold">${d.dishName}</span><br><small class="text-muted">${d.quantity} x ${d.unitPrice.toLocaleString()}</small>${d.note ? `<br><small class="text-warning">"${d.note}"</small>` : ''}</div><div class="text-end"><div class="fw-bold">${d.totalAmount.toLocaleString()}</div><span class="badge ${badge}">${txt}</span></div>`;
                container.appendChild(div);
            });
        } else { container.innerHTML = `<div class="text-center text-muted">Không có món ăn</div>`; }
    } catch (e) { container.innerHTML = `<div class="text-danger text-center">Lỗi tải dữ liệu</div>`; }
}
async function loadMenuData() { try { const res = await fetch(`${API_URL}/Menu`); appState.categories = await res.json(); } catch (e) { } }
function openMenuSelection() { appState.tempMenuSelection = {}; renderMenuUI(); showView('view-menu'); }
function renderMenuUI() {
    const catBar = document.getElementById('categoryBar');
    const dishList = document.getElementById('dishList');
    catBar.innerHTML = ''; dishList.innerHTML = '';
    appState.categories.forEach((cat, idx) => {
        const btn = document.createElement('button');
        btn.className = `filter-btn ${idx === 0 ? 'active' : ''}`;
        btn.innerText = cat.categoryName;
        btn.onclick = (e) => { document.querySelectorAll('#categoryBar .filter-btn').forEach(b => b.classList.remove('active')); e.target.classList.add('active'); document.getElementById(`cat-${cat.categoryID}`).scrollIntoView({ behavior: 'smooth' }); };
        catBar.appendChild(btn);
        const h6 = document.createElement('h6'); h6.className = 'bg-light p-2 m-0 border-top border-bottom fw-bold text-secondary'; h6.innerText = cat.categoryName; h6.id = `cat-${cat.categoryID}`; dishList.appendChild(h6);
        (cat.dishes || cat.Dishes || []).forEach(dish => {
            const div = document.createElement('div'); div.className = 'dish-item'; div.dataset.id = dish.dishID;
            const curQty = appState.tempMenuSelection[dish.dishID] || 0;
            div.innerHTML = `<div class="dish-info" onclick="incrementDish(${dish.dishID})"><h6>${dish.dishName}</h6><div class="dish-price">${dish.price.toLocaleString()}đ</div></div><div class="qty-control">${curQty > 0 ? `<button class="btn-qty" onclick="updateTempQty(${dish.dishID},-1)">-</button><span class="fw-bold">${curQty}</span>` : ''}<button class="btn-qty ${curQty > 0 ? 'active' : ''}" onclick="updateTempQty(${dish.dishID},1)">+</button></div>`;
            dishList.appendChild(div);
        });
    });
    updateMenuActionBar();
}
function updateTempQty(id, delta) { appState.tempMenuSelection[id] = (appState.tempMenuSelection[id] || 0) + delta; if (appState.tempMenuSelection[id] <= 0) delete appState.tempMenuSelection[id]; renderMenuUI(); }
function incrementDish(id) { updateTempQty(id, 1); }
function updateMenuActionBar() { const bar = document.getElementById('menuActionBar'); let total = 0; for (let k in appState.tempMenuSelection) total += appState.tempMenuSelection[k]; if (total > 0) { bar.style.display = 'flex'; document.getElementById('selectedCount').innerText = total; } else bar.style.display = 'none'; }
// --- CONFIRM MENU (GỌI API THÊM VÀO GIỎ SERVER) ---
async function confirmMenuSelection() {
    const itemsToAdd = [];
    for (const [idStr, qty] of Object.entries(appState.tempMenuSelection)) {
        itemsToAdd.push({
            dishID: parseInt(idStr),
            quantity: qty,
            note: ""
        });
    }

    if (itemsToAdd.length === 0) return;

    // Logic: Nếu chưa có order (list rỗng) -> Gọi /create, ngược lại gọi /add
    // Dựa trên DTO của bạn: Create dùng Items, Add dùng Details
    let url, payload;

    if (appState.orderDetails.length === 0) {
        url = `${API_URL}/Order/create`;
        payload = {
            tableID: appState.currentTableId,
            accID: currentUser.accID || 1,
            items: itemsToAdd // Dùng Items theo OrderRequest.cs
        };
    } else {
        url = `${API_URL}/Order/${appState.currentTableId}/add`;
        payload = {
            details: itemsToAdd // Dùng Details theo AddOrderItemsRequest.cs
        };
    }

    try {
        const res = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (res.ok) {
            showToast("Đã thêm vào giỏ (Chờ gửi bếp)");
            appState.tempMenuSelection = {};
            showView('view-detail');
            document.querySelector('a[href="#tab-cart"]').click(); // Chuyển tab Cart
            loadOrderData(appState.currentTableId); // Reload
        } else {
            const txt = await res.text();
            showToast("Lỗi: " + txt, 'danger');
        }
    } catch (e) {
        showToast("Lỗi kết nối", 'danger');
    }
}
function cancelMenuSelection() { appState.tempMenuSelection = {}; showView('view-detail'); }
function searchMenu() { const v = document.getElementById('searchDish').value.toLowerCase(); document.querySelectorAll('.dish-item').forEach(i => i.style.display = i.querySelector('h6').innerText.toLowerCase().includes(v) ? 'flex' : 'none'); }
function logout() { localStorage.removeItem('posUser'); window.location.href = 'index.html'; }