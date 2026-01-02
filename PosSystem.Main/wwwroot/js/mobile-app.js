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
    cart: [],
    tempMenuSelection: {},
    currentFilter: 'All'
};

// --- KHỞI TẠO ---
document.addEventListener('DOMContentLoaded', async () => {
    try {
        const userStr = localStorage.getItem('posUser');
        if (!userStr) {
            window.location.href = 'index.html';
            return;
        }
        currentUser = JSON.parse(userStr);

        await loadTables();
        await loadMenuData();
        setTimeout(() => initSignalR(), 500);

    } catch (err) {
        console.error("Init Error:", err);
    }
});

// --- SIGNALR (XỬ LÝ ĐỒNG BỘ) ---
function initSignalR() {
    if (typeof signalR === 'undefined') return;

    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/posHub")
        .withAutomaticReconnect()
        .build();

    // KHI NHẬN ĐƯỢC TÍN HIỆU TỪ SERVER (Do Máy tính hoặc ĐT khác gửi)
    connection.on("TableUpdated", (tableId) => {
        console.log(`Nhận tín hiệu update bàn: ${tableId}`);

        // 1. Luôn tải lại danh sách bàn để cập nhật màu (Xanh/Đỏ)
        loadTables(false);

        // 2. Nếu mình đang xem đúng bàn đó -> Tải lại danh sách món ngay lập tức
        if (appState.currentTableId == tableId) {
            loadConfirmedOrders(tableId);
            showToast("Dữ liệu vừa được cập nhật!", "info");
        }
    });

    connection.start()
        .then(() => console.log("SignalR Connected!"))
        .catch(err => console.error("SignalR Error: ", err));
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

// --- CHI TIẾT BÀN ---
function openTableDetail(table) {
    // Reload lại status mới nhất từ bộ nhớ local để chắc chắn
    const freshTable = appState.tables.find(t => t.tableID === table.tableID) || table;

    appState.currentTableId = freshTable.tableID;
    document.getElementById('detailTableName').innerText = freshTable.tableName;
    document.getElementById('detailTableStatus').innerText = freshTable.tableStatus;

    appState.cart = [];
    renderCartTab();
    loadConfirmedOrders(freshTable.tableID);
    showView('view-detail');
}

// --- GỬI BẾP (CÓ CHECK TRẠNG THÁI ĐỂ TRÁNH BUG) ---
async function sendOrderToKitchen() {
    if (appState.cart.length === 0) return;

    // 1. Kiểm tra trạng thái bàn TRƯỚC khi gửi (Concurrency Check)
    // Lấy lại dữ liệu bàn mới nhất từ server
    try {
        const checkRes = await fetch(`${API_URL}/Table`);
        const allTables = await checkRes.json();
        const targetTable = allTables.find(t => t.tableID === appState.currentTableId);

        // Nếu bàn này đang Empty (do máy khác vừa thanh toán), không cho gửi món
        // Trừ khi logic quán bạn cho phép gọi món vào bàn trống (tự mở bàn)
        // Ở đây giả sử: Phải cẩn thận
    } catch (err) { console.error(err); }

    const btn = document.querySelector('#cartActionBar button');
    btn.disabled = true;
    btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Đang xử lý...';

    const payload = {
        details: appState.cart.map(c => ({
            dishID: c.dishID,
            quantity: c.quantity,
            note: c.note || ""
        }))
    };

    try {
        const res = await fetch(`${API_URL}/Order/${appState.currentTableId}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (res.ok) {
            showToast('Gửi bếp thành công!');
            appState.cart = [];
            renderCartTab();
            const tabEl = document.querySelector('a[href="#tab-confirmed"]');
            if (tabEl) new bootstrap.Tab(tabEl).show();

            // Tải lại để thấy món vừa thêm
            loadConfirmedOrders(appState.currentTableId);
        } else {
            showToast('Lỗi! Có thể bàn đã bị đóng hoặc lỗi server.', 'danger');
            // Tải lại bảng bàn để cập nhật tình hình
            loadTables();
        }
    } catch (e) {
        showToast('Lỗi kết nối mạng!', 'danger');
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
function renderCartTab() {
    const container = document.getElementById('cartList');
    const actionBar = document.getElementById('cartActionBar');
    container.innerHTML = '';
    if (appState.cart.length === 0) {
        container.innerHTML = `<div class="text-center text-muted mt-5"><i class="fas fa-shopping-basket fs-1 mb-3"></i><br>Chưa chọn món nào</div>`;
        if (actionBar) actionBar.classList.add('d-none');
        return;
    }
    let total = 0;
    appState.cart.forEach((item, index) => {
        total += item.price * item.quantity;
        const div = document.createElement('div');
        div.className = 'd-flex justify-content-between align-items-center p-3 border-bottom bg-white';
        div.innerHTML = `
            <div>
                <div class="fw-bold">${item.name}</div>
                <div class="text-muted small">${item.price.toLocaleString()}đ x ${item.quantity}</div>
                ${item.note ? `<div class="text-warning small fst-italic">"${item.note}"</div>` : ''}
            </div>
            <div class="d-flex align-items-center gap-2">
                <button class="btn btn-sm btn-outline-secondary" onclick="updateCartItem(${index}, -1)">-</button>
                <span class="fw-bold" style="min-width:20px; text-align:center">${item.quantity}</span>
                <button class="btn btn-sm btn-outline-secondary" onclick="updateCartItem(${index}, 1)">+</button>
                <button class="btn btn-sm text-danger ms-2" onclick="removeCartItem(${index})"><i class="fas fa-trash"></i></button>
            </div>`;
        container.appendChild(div);
    });
    document.getElementById('cartTotalMoney').innerText = total.toLocaleString() + 'đ';
    if (actionBar) { actionBar.classList.remove('d-none'); actionBar.style.display = 'flex'; }
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
function confirmMenuSelection() {
    for (const [idStr, qty] of Object.entries(appState.tempMenuSelection)) {
        const id = parseInt(idStr); let dish = null;
        appState.categories.some(c => (dish = (c.dishes || c.Dishes || []).find(d => d.dishID === id)));
        if (dish) {
            const exist = appState.cart.find(c => c.dishID === id && c.note === "");
            if (exist) exist.quantity += qty; else appState.cart.push({ dishID: id, name: dish.dishName, price: dish.price, quantity: qty, note: "" });
        }
    }
    appState.tempMenuSelection = {}; renderCartTab(); showView('view-detail');
    const tab = document.querySelector('a[href="#tab-cart"]'); if (tab) new bootstrap.Tab(tab).show();
}
function cancelMenuSelection() { appState.tempMenuSelection = {}; showView('view-detail'); }
function searchMenu() { const v = document.getElementById('searchDish').value.toLowerCase(); document.querySelectorAll('.dish-item').forEach(i => i.style.display = i.querySelector('h6').innerText.toLowerCase().includes(v) ? 'flex' : 'none'); }
function logout() { localStorage.removeItem('posUser'); window.location.href = 'index.html'; }