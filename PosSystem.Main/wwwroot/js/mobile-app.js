// --- PHIÊN BẢN V6: FILTER ĐỘNG TỪ DATABASE + FIX SIGNALR ---
const API_URL = '/api';

// CẤU HÌNH TÊN HIỂN THỊ CHO CÁC LOẠI BÀN
const TABLE_TYPE_NAMES = {
    'DineIn': 'Tại quán',
    'TakeAway': 'Mang về',
    'Pickup': 'Khách lấy',
    'Delivery': 'Ship',
    'VIP': 'Phòng VIP' // Ví dụ thêm nếu sau này cần
};

let currentUser = null;
let appState = {
    tables: [],
    categories: [],
    currentTableId: null,
    cart: [],
    tempMenuSelection: {},
    currentFilter: 'All' // Lưu trạng thái lọc hiện tại
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

        // 1. Tải dữ liệu
        await loadTables();
        await loadMenuData();

        // 2. Khởi động SignalR
        setTimeout(() => initSignalR(), 500);

    } catch (err) {
        console.error("Lỗi khởi động:", err);
    }
});

// --- SIGNALR ---
function initSignalR() {
    if (typeof signalR === 'undefined') {
        console.warn("Chưa tải được SignalR.");
        return;
    }

    try {
        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/posHub")
            .withAutomaticReconnect()
            .build();

        connection.on("TableUpdated", (tableId) => {
            if (appState.currentTableId == tableId) {
                loadConfirmedOrders(tableId);
            }
            loadTables(false); // false: reload data nhưng không vẽ lại filter bar để tránh giật
        });

        connection.start()
            .then(() => console.log("SignalR Connected!"))
            .catch(err => console.error("SignalR Error: ", err));

    } catch (e) {
        console.error("SignalR Exception:", e);
    }
}

// --- NAVIGATION ---
function showView(viewId) {
    document.querySelectorAll('.view-section').forEach(el => el.classList.remove('active'));
    const el = document.getElementById(viewId);
    if (el) el.classList.add('active');
}

function showToast(msg, type = 'success') {
    const toastEl = document.getElementById('liveToast');
    if (toastEl) {
        document.getElementById('toastMessage').innerText = msg;
        toastEl.className = `toast align-items-center text-white bg-${type} border-0`;
        const toast = new bootstrap.Toast(toastEl);
        toast.show();
    }
}

// --- LOGIC BÀN & FILTER ---
async function loadTables(shouldRenderFilters = true) {
    try {
        const res = await fetch(`${API_URL}/Table`);
        if (!res.ok) throw new Error(`Lỗi Server: ${res.status}`);

        const data = await res.json();
        appState.tables = data;

        // Nếu là lần đầu tải, hoặc cần vẽ lại thanh filter
        if (shouldRenderFilters) {
            renderFilterButtons();
        }

        // Vẽ lại danh sách bàn theo filter hiện tại
        renderTables(appState.currentFilter);
    } catch (e) {
        console.error(e);
        const grid = document.getElementById('tableGrid');
        if (grid) grid.innerHTML = `<div class="text-danger text-center p-3">Mất kết nối server!<br><button class="btn btn-sm btn-outline-danger mt-2" onclick="loadTables()">Thử lại</button></div>`;
    }
}

// Hàm mới: Vẽ nút lọc dựa trên dữ liệu thực tế
function renderFilterButtons() {
    const filterContainer = document.getElementById('tableFilters');
    if (!filterContainer) return;

    filterContainer.innerHTML = '';

    // 1. Luôn có nút "Tất cả"
    const btnAll = document.createElement('button');
    btnAll.className = `filter-btn ${appState.currentFilter === 'All' ? 'active' : ''}`;
    btnAll.innerText = 'Tất cả';
    btnAll.onclick = () => filterTables('All');
    filterContainer.appendChild(btnAll);

    // 2. Tìm các loại bàn duy nhất có trong Database
    // Set giúp lọc trùng lặp
    const uniqueTypes = [...new Set(appState.tables.map(t => t.tableType))];

    // 3. Tạo nút cho từng loại
    uniqueTypes.forEach(type => {
        if (!type) return; // Bỏ qua nếu null

        const btn = document.createElement('button');
        // Kiểm tra xem nút này có đang active không
        btn.className = `filter-btn ${appState.currentFilter === type ? 'active' : ''}`;

        // Dịch tên sang tiếng Việt (nếu không có trong từ điển thì hiện nguyên gốc)
        btn.innerText = TABLE_TYPE_NAMES[type] || type;

        btn.onclick = () => filterTables(type);
        filterContainer.appendChild(btn);
    });
}

function renderTables(filterType) {
    const grid = document.getElementById('tableGrid');
    if (!grid) return;
    grid.innerHTML = '';

    // Cập nhật trạng thái active của nút bấm UI
    appState.currentFilter = filterType;
    const btns = document.querySelectorAll('#tableFilters .filter-btn');
    btns.forEach(b => {
        // So sánh text của button hoặc logic click
        // Cách đơn giản: Reset hết, sau đó highlight nút dựa trên logic vẽ lại ở renderFilterButtons
        // Nhưng để mượt mà, ta update class tại đây luôn
        b.classList.remove('active');
        // Hack nhẹ: tìm nút có text tương ứng hoặc onclick tương ứng
        // Tuy nhiên, để đơn giản, ta tin tưởng renderFilterButtons() vẽ đúng class ban đầu.
        // Tại đây ta chỉ cần highlight nút vừa bấm.
        if (window.event && window.event.target === b) {
            b.classList.add('active');
        } else if (b.innerText === 'Tất cả' && filterType === 'All') {
            b.classList.add('active');
        } else if (TABLE_TYPE_NAMES[filterType] === b.innerText) {
            b.classList.add('active');
        }
    });

    const filtered = filterType === 'All'
        ? appState.tables
        : appState.tables.filter(t => t.tableType === filterType);

    if (filtered.length === 0) {
        grid.innerHTML = '<div class="text-muted text-center w-100 mt-5">Không có bàn nào loại này.</div>';
        return;
    }

    filtered.forEach(t => {
        const div = document.createElement('div');
        const tName = t.tableName || "Bàn ?";
        const tStatus = t.tableStatus || "Empty";
        const isOccupied = tStatus === 'Occupied';

        // Icon thay đổi theo loại bàn (Optional - làm đẹp thêm)
        let icon = 'fa-chair';
        if (t.tableType === 'Delivery') icon = 'fa-motorcycle';
        if (t.tableType === 'TakeAway') icon = 'fa-shopping-bag';
        if (t.tableType === 'Pickup') icon = 'fa-walking';

        div.className = `table-card ${isOccupied ? 'occupied' : ''}`;
        div.onclick = () => openTableDetail(t);

        div.innerHTML = `
            <div class="fs-4 mb-1"><i class="fas ${icon}"></i></div>
            <div class="fw-bold">${tName}</div>
            <small class="${isOccupied ? 'text-danger' : 'text-success'}">
                ${isOccupied ? 'Có khách' : 'Trống'}
            </small>
        `;
        grid.appendChild(div);
    });
}
function filterTables(type) { renderTables(type); }

// --- LOGIC CHI TIẾT BÀN ---
function openTableDetail(table) {
    if (!table) return;
    appState.currentTableId = table.tableID;

    const nameEl = document.getElementById('detailTableName');
    if (nameEl) nameEl.innerText = table.tableName;

    const statusEl = document.getElementById('detailTableStatus');
    if (statusEl) statusEl.innerText = table.tableStatus;

    appState.cart = [];
    renderCartTab();
    loadConfirmedOrders(table.tableID);
    showView('view-detail');
}

// --- TAB: CART ---
function renderCartTab() {
    const container = document.getElementById('cartList');
    const actionBar = document.getElementById('cartActionBar');
    if (!container) return;

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
            </div>
        `;
        container.appendChild(div);
    });

    const totalEl = document.getElementById('cartTotalMoney');
    if (totalEl) totalEl.innerText = total.toLocaleString() + 'đ';

    if (actionBar) {
        actionBar.classList.remove('d-none');
        actionBar.style.display = 'flex';
    }
}

function updateCartItem(index, delta) {
    appState.cart[index].quantity += delta;
    if (appState.cart[index].quantity <= 0) appState.cart.splice(index, 1);
    renderCartTab();
}
function removeCartItem(index) {
    appState.cart.splice(index, 1);
    renderCartTab();
}

async function sendOrderToKitchen() {
    if (appState.cart.length === 0) return;
    const btn = document.querySelector('#cartActionBar button');
    if (btn) btn.disabled = true;

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
            if (tabEl) {
                const tab = new bootstrap.Tab(tabEl);
                tab.show();
            }
            loadConfirmedOrders(appState.currentTableId);
        } else {
            showToast('Lỗi gửi đơn!', 'danger');
        }
    } catch (e) {
        showToast('Lỗi kết nối!', 'danger');
    } finally {
        if (btn) btn.disabled = false;
    }
}

async function loadConfirmedOrders(tableId) {
    const container = document.getElementById('confirmedList');
    if (!container) return;
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
            const groupedDetails = [];
            data.details.forEach(item => {
                const currentNote = (item.note || "").trim();
                const existing = groupedDetails.find(g =>
                    g.dishID === item.dishID &&
                    (g.note || "").trim() === currentNote
                );
                if (existing) {
                    existing.quantity += item.quantity;
                    existing.totalAmount += item.totalAmount;
                } else {
                    groupedDetails.push({ ...item });
                }
            });

            groupedDetails.forEach(d => {
                let badgeClass = 'bg-secondary';
                let statusText = d.itemStatus;
                if (d.itemStatus === 'New') { badgeClass = 'bg-primary'; statusText = 'Mới'; }
                else if (d.itemStatus === 'Sent') { badgeClass = 'bg-info text-dark'; statusText = 'Đã gửi'; }
                else if (d.itemStatus === 'Done') { badgeClass = 'bg-success'; statusText = 'Đã ra'; }

                const div = document.createElement('div');
                div.className = 'd-flex justify-content-between p-2 border-bottom';
                div.innerHTML = `
                    <div>
                        <span class="fw-bold">${d.dishName}</span> <br>
                        <small class="text-muted">${d.quantity} x ${d.unitPrice.toLocaleString()}</small>
                        ${d.note ? `<br><small class="text-warning fst-italic">"${d.note}"</small>` : ''}
                    </div>
                    <div class="text-end">
                        <div class="fw-bold">${d.totalAmount.toLocaleString()}</div>
                        <span class="badge ${badgeClass}">${statusText}</span>
                    </div>
                `;
                container.appendChild(div);
            });
        } else {
            container.innerHTML = `<div class="text-center text-muted">Không có món ăn</div>`;
        }
    } catch (e) {
        console.error(e);
        container.innerHTML = `<div class="text-center text-danger">Lỗi tải dữ liệu</div>`;
    }
}

// --- MENU ---
async function loadMenuData() {
    try {
        const res = await fetch(`${API_URL}/Menu`);
        appState.categories = await res.json();
    } catch (e) { console.error(e); }
}

function openMenuSelection() {
    appState.tempMenuSelection = {};
    renderMenuUI();
    showView('view-menu');
}

function renderMenuUI() {
    const catBar = document.getElementById('categoryBar');
    const dishList = document.getElementById('dishList');
    if (!catBar || !dishList) return;

    catBar.innerHTML = '';
    dishList.innerHTML = '';

    appState.categories.forEach((cat, idx) => {
        const btn = document.createElement('button');
        btn.className = `filter-btn ${idx === 0 ? 'active' : ''}`;
        btn.innerText = cat.categoryName;
        btn.onclick = (e) => {
            document.querySelectorAll('#categoryBar .filter-btn').forEach(b => b.classList.remove('active'));
            e.target.classList.add('active');
            const target = document.getElementById(`cat-${cat.categoryID}`);
            if (target) target.scrollIntoView({ behavior: 'smooth', block: 'start' });
        };
        catBar.appendChild(btn);

        const catHeader = document.createElement('h6');
        catHeader.className = 'bg-light p-2 m-0 border-top border-bottom text-uppercase text-secondary fw-bold';
        catHeader.innerText = cat.categoryName;
        catHeader.id = `cat-${cat.categoryID}`;
        dishList.appendChild(catHeader);

        const dishes = cat.dishes || cat.Dishes || [];
        dishes.forEach(dish => {
            const wrapper = document.createElement('div');
            wrapper.className = 'dish-item';
            wrapper.dataset.id = dish.dishID;

            const currentQty = appState.tempMenuSelection[dish.dishID] || 0;
            const activeClass = currentQty > 0 ? 'active' : '';

            wrapper.innerHTML = `
                <div class="dish-info" onclick="incrementDish(${dish.dishID})">
                    <h6>${dish.dishName}</h6>
                    <div class="dish-price">${dish.price.toLocaleString()}đ</div>
                </div>
                <div class="qty-control">
                    ${currentQty > 0 ? `
                        <button class="btn-qty" onclick="updateTempQty(${dish.dishID}, -1)">-</button>
                        <span id="qty-${dish.dishID}" class="fw-bold">${currentQty}</span>
                    ` : ''}
                    <button class="btn-qty ${activeClass}" onclick="updateTempQty(${dish.dishID}, 1)">+</button>
                </div>
            `;
            dishList.appendChild(wrapper);
        });
    });
    updateMenuActionBar();
}

function updateTempQty(dishID, delta) {
    if (!appState.tempMenuSelection[dishID]) appState.tempMenuSelection[dishID] = 0;
    appState.tempMenuSelection[dishID] += delta;
    if (appState.tempMenuSelection[dishID] <= 0) delete appState.tempMenuSelection[dishID];
    renderMenuUI();
}
function incrementDish(id) { updateTempQty(id, 1); }

function updateMenuActionBar() {
    const bar = document.getElementById('menuActionBar');
    if (!bar) return;
    let totalItems = 0;
    for (let key in appState.tempMenuSelection) totalItems += appState.tempMenuSelection[key];

    if (totalItems > 0) {
        bar.style.display = 'flex';
        document.getElementById('selectedCount').innerText = totalItems;
    } else {
        bar.style.display = 'none';
    }
}

function confirmMenuSelection() {
    for (const [dishIdStr, qty] of Object.entries(appState.tempMenuSelection)) {
        const dishID = parseInt(dishIdStr);
        let foundDish = null;
        appState.categories.some(cat => {
            const list = cat.dishes || cat.Dishes || [];
            foundDish = list.find(d => d.dishID === dishID);
            return foundDish;
        });

        if (foundDish) {
            const existing = appState.cart.find(c => c.dishID === dishID && c.note === "");
            if (existing) {
                existing.quantity += qty;
            } else {
                appState.cart.push({
                    dishID: dishID,
                    name: foundDish.dishName,
                    price: foundDish.price,
                    quantity: qty,
                    note: ""
                });
            }
        }
    }
    appState.tempMenuSelection = {};
    renderCartTab();
    showView('view-detail');
    const tabEl = document.querySelector('a[href="#tab-cart"]');
    if (tabEl) {
        const tab = new bootstrap.Tab(tabEl);
        tab.show();
    }
}

function cancelMenuSelection() {
    appState.tempMenuSelection = {};
    showView('view-detail');
}

function searchMenu() {
    const term = document.getElementById('searchDish').value.toLowerCase();
    document.querySelectorAll('.dish-item').forEach(item => {
        const nameEl = item.querySelector('h6');
        if (nameEl) {
            const name = nameEl.innerText.toLowerCase();
            item.style.display = name.includes(term) ? 'flex' : 'none';
        }
    });
}

function logout() {
    localStorage.removeItem('posUser');
    window.location.href = 'index.html';
}