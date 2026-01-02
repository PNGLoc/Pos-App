const API_URL = '/api';
const TABLE_TYPE_NAMES = { 'DineIn': 'Tại quán', 'TakeAway': 'Mang về', 'Pickup': 'Khách lấy', 'Delivery': 'Ship' };

let currentUser = null;
let appState = {
    tables: [],
    categories: [],
    currentTableId: null,
    orderDetails: [], // Dữ liệu từ server
    tempMenuSelection: {}, // Lưu tạm món đang chọn: { dishID: { qty: 1, note: '' } }
    currentFilter: 'All'
};

document.addEventListener('DOMContentLoaded', async () => {
    const userStr = localStorage.getItem('posUser');
    if (!userStr) { window.location.href = 'index.html'; return; }
    currentUser = JSON.parse(userStr);

    await loadTables();
    await loadMenuData();
    setTimeout(() => initSignalR(), 500);
});

function initSignalR() {
    if (typeof signalR === 'undefined') return;
    const connection = new signalR.HubConnectionBuilder().withUrl("/posHub").withAutomaticReconnect().build();
    connection.on("TableUpdated", (tableId) => {
        loadTables(false);
        if (appState.currentTableId == tableId) loadOrderData(tableId);
    });
    connection.start().catch(err => console.error(err));
}

// --- TIỆN ÍCH SEARCH ---
function removeAccents(str) { return str.normalize("NFD").replace(/[\u0300-\u036f]/g, ""); }
function getAcronym(str) { return removeAccents(str).toLowerCase().split(/\s+/).map(w => w[0]).join(''); }

// --- NAV & UI ---
function showView(viewId) { document.querySelectorAll('.view-section').forEach(el => el.classList.remove('active')); document.getElementById(viewId).classList.add('active'); }
function showToast(msg, type = 'success') {
    const toastEl = document.getElementById('liveToast');
    if (toastEl) {
        document.getElementById('toastMessage').innerText = msg;
        toastEl.className = `toast align-items-center text-white bg-${type} border-0`;
        new bootstrap.Toast(toastEl).show();
    }
}

// --- LOGIC BÀN ---
async function loadTables(renderFilter = true) {
    try { const res = await fetch(`${API_URL}/Table`); appState.tables = await res.json(); if (renderFilter) renderFilterButtons(); renderTables(appState.currentFilter); } catch (e) { }
}
function renderTables(filterType) {
    const grid = document.getElementById('tableGrid'); grid.innerHTML = '';
    const filtered = filterType === 'All' ? appState.tables : appState.tables.filter(t => t.tableType === filterType);
    document.querySelectorAll('#tableFilters .filter-btn').forEach(b => b.classList.remove('active'));
    for (let b of document.querySelectorAll('#tableFilters .filter-btn')) {
        if ((filterType === 'All' && b.innerText === 'Tất cả') || (TABLE_TYPE_NAMES[filterType] === b.innerText)) { b.classList.add('active'); break; }
    }
    filtered.forEach(t => {
        const div = document.createElement('div'); div.className = `table-card ${t.tableStatus === 'Occupied' ? 'occupied' : ''}`; div.onclick = () => openTableDetail(t);
        div.innerHTML = `<div class="fs-4 mb-1"><i class="fas fa-chair"></i></div><div class="fw-bold">${t.tableName}</div><small class="${t.tableStatus === 'Occupied' ? 'text-danger' : 'text-success'}">${t.tableStatus === 'Occupied' ? 'Có khách' : 'Trống'}</small>`; grid.appendChild(div);
    });
}
function renderFilterButtons() {
    const filterContainer = document.getElementById('tableFilters'); if (!filterContainer) return; filterContainer.innerHTML = '';
    const btnAll = document.createElement('button'); btnAll.className = `filter-btn active`; btnAll.innerText = 'Tất cả'; btnAll.onclick = () => filterTables('All'); filterContainer.appendChild(btnAll);
    [...new Set(appState.tables.map(t => t.tableType))].forEach(type => { if (!type) return; const btn = document.createElement('button'); btn.className = `filter-btn`; btn.innerText = TABLE_TYPE_NAMES[type] || type; btn.onclick = () => filterTables(type); filterContainer.appendChild(btn); });
}
function filterTables(type) { appState.currentFilter = type; renderTables(type); }

function openTableDetail(table) {
    appState.currentTableId = table.tableID;
    document.getElementById('detailTableName').innerText = table.tableName;
    document.getElementById('detailTableStatus').innerText = table.tableStatus;
    loadOrderData(table.tableID);
    showView('view-detail');
}

// --- LOGIC ORDER (CART & CONFIRMED) ---
async function loadOrderData(tableId) {
    try {
        const res = await fetch(`${API_URL}/Order/${tableId}`);
        if (res.ok) {
            const data = await res.json();
            appState.orderDetails = data.Details || data.details || [];
        } else { appState.orderDetails = []; }
    } catch (e) { appState.orderDetails = []; }

    // Tính tổng tiền món đã chốt (không phải New)
    const confirmedTotal = appState.orderDetails.filter(d => d.itemStatus !== 'New').reduce((sum, d) => sum + d.totalAmount, 0);
    document.getElementById('confirmedTotal').innerText = confirmedTotal.toLocaleString() + 'đ';

    renderCartTab(); renderConfirmedTab();
}

// --- TAB CART (MÓN STATUS = NEW) ---
function renderCartTab() {
    const container = document.getElementById('cartList');
    const actionBar = document.getElementById('cartActionBar');
    container.innerHTML = '';
    const cartItems = appState.orderDetails.filter(d => d.itemStatus === 'New');

    if (cartItems.length === 0) {
        container.innerHTML = `<div class="text-center text-muted mt-5"><i class="fas fa-shopping-basket fs-1 mb-3"></i><br>Giỏ hàng trống</div>`;
        actionBar.classList.remove('d-flex'); actionBar.classList.add('d-none'); return;
    }

    let total = 0;
    cartItems.forEach((item) => {
        total += item.totalAmount;
        const div = document.createElement('div');
        div.className = 'd-flex flex-column p-3 border-bottom bg-white';
        div.innerHTML = `
            <div class="d-flex justify-content-between align-items-start mb-2">
                <div>
                    <div class="fw-bold">${item.dishName}</div>
                    <div class="text-muted small">${item.unitPrice.toLocaleString()}đ</div>
                    ${item.note ? `<div class="text-warning small fst-italic cursor-pointer" onclick="openNoteModal(${item.orderDetailID}, 'cart', '${item.note}')"><i class="fas fa-pen small"></i> ${item.note}</div>` : ''}
                </div>
                <div class="fw-bold">${item.totalAmount.toLocaleString()}đ</div>
            </div>
            <div class="d-flex justify-content-between align-items-center">
                <div class="btn-group btn-group-sm">
                    <button class="btn btn-outline-secondary" onclick="updateCartItem(${item.orderDetailID}, ${item.quantity - 1}, '${item.note || ''}')"><i class="fas fa-minus"></i></button>
                    <button class="btn btn-outline-secondary disabled fw-bold text-dark" style="min-width:30px">${item.quantity}</button>
                    <button class="btn btn-outline-secondary" onclick="updateCartItem(${item.orderDetailID}, ${item.quantity + 1}, '${item.note || ''}')"><i class="fas fa-plus"></i></button>
                </div>
                <div class="d-flex gap-2">
                    <button class="btn btn-sm btn-outline-warning" onclick="openNoteModal(${item.orderDetailID}, 'cart', '${item.note || ''}')"><i class="fas fa-comment-dots"></i></button>
                    <button class="btn btn-sm btn-outline-danger" onclick="updateCartItem(${item.orderDetailID}, 0, '')"><i class="fas fa-trash"></i></button>
                </div>
            </div>`;
        container.appendChild(div);
    });
    document.getElementById('cartTotalMoney').innerText = total.toLocaleString() + 'đ';
    actionBar.classList.remove('d-none'); actionBar.classList.add('d-flex');
}

async function updateCartItem(detailId, newQty, note) {
    try {
        const res = await fetch(`${API_URL}/Order/${appState.currentTableId}/update-item`, {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ orderDetailID: detailId, quantity: newQty, note: note })
        });
        if (res.ok) loadOrderData(appState.currentTableId);
        else showToast(await res.text(), 'danger');
    } catch (e) { showToast("Lỗi kết nối", 'danger'); }
}

// --- TAB CONFIRMED ---
function renderConfirmedTab() {
    const container = document.getElementById('confirmedList'); container.innerHTML = '';
    const items = appState.orderDetails.filter(d => d.itemStatus !== 'New');
    if (items.length === 0) { container.innerHTML = `<div class="text-center text-muted mt-5">Chưa có món nào được gọi</div>`; return; }
    const grouped = [];
    items.forEach(item => {
        const key = `${item.dishID}_${(item.note || "").trim()}_${item.itemStatus}`;
        const exist = grouped.find(g => `${g.dishID}_${(g.note || "").trim()}_${g.itemStatus}` === key);
        if (exist) { exist.quantity += item.quantity; exist.totalAmount += item.totalAmount; } else grouped.push({ ...item });
    });
    grouped.forEach(d => {
        let badge = 'bg-secondary', txt = d.itemStatus;
        if (d.itemStatus === 'Sent') { badge = 'bg-info text-dark'; txt = 'Đã gửi'; }
        else if (d.itemStatus === 'Done') { badge = 'bg-success'; txt = 'Đã ra'; }
        container.innerHTML += `<div class="d-flex justify-content-between p-2 border-bottom"><div><span class="fw-bold">${d.dishName}</span> <br><small class="text-muted">${d.quantity} x ${(d.totalAmount / d.quantity).toLocaleString()}</small>${d.note ? `<br><small class="text-warning fst-italic">"${d.note}"</small>` : ''}</div><div class="text-end"><div class="fw-bold">${d.totalAmount.toLocaleString()}</div><span class="badge ${badge}">${txt}</span></div></div>`;
    });
}

// --- MENU & SELECTION ---
async function loadMenuData() { const res = await fetch(`${API_URL}/Menu`); appState.categories = await res.json(); }
function openMenuSelection() { appState.tempMenuSelection = {}; renderMenuUI(); showView('view-menu'); }

function renderMenuUI() {
    const catBar = document.getElementById('categoryBar'); const dishList = document.getElementById('dishList');
    catBar.innerHTML = ''; dishList.innerHTML = '';

    // 1. Tab Tất Cả
    const btnAll = document.createElement('button'); btnAll.className = `filter-btn active`; btnAll.innerText = "Tất cả";
    btnAll.onclick = (e) => { document.querySelectorAll('#categoryBar .filter-btn').forEach(b => b.classList.remove('active')); e.target.classList.add('active'); document.getElementById('view-menu').scrollTo({ top: 0, behavior: 'smooth' }); }; catBar.appendChild(btnAll);

    appState.categories.forEach((cat) => {
        const btn = document.createElement('button'); btn.className = `filter-btn`; btn.innerText = cat.categoryName;
        btn.onclick = (e) => { document.querySelectorAll('#categoryBar .filter-btn').forEach(b => b.classList.remove('active')); e.target.classList.add('active'); document.getElementById(`cat-${cat.categoryID}`).scrollIntoView({ behavior: 'smooth', block: 'start' }); }; catBar.appendChild(btn);
        const catHeader = document.createElement('h6'); catHeader.className = 'bg-light p-2 m-0 border-top border-bottom text-uppercase text-secondary fw-bold'; catHeader.innerText = cat.categoryName; catHeader.id = `cat-${cat.categoryID}`; dishList.appendChild(catHeader);

        (cat.dishes || cat.Dishes || []).forEach(dish => {
            const wrapper = document.createElement('div'); wrapper.dataset.id = dish.dishID;
            const selection = appState.tempMenuSelection[dish.dishID]; const qty = selection ? selection.qty : 0; const note = selection ? selection.note : "";

            // 2. UI Chọn món mới (Click hiện controls)
            if (qty === 0) {
                wrapper.className = 'dish-item';
                wrapper.innerHTML = `<div class="w-100" onclick="incrementDish(${dish.dishID})"><div class="d-flex justify-content-between align-items-center"><h6 class="m-0">${dish.dishName}</h6><div class="fw-bold text-primary">${dish.price.toLocaleString()}đ</div></div></div>`;
            } else {
                wrapper.className = 'dish-item bg-light border border-primary';
                wrapper.innerHTML = `<div class="w-100"><div class="d-flex justify-content-between align-items-center mb-2"><h6 class="m-0 text-primary fw-bold">${dish.dishName}</h6><div class="fw-bold">${dish.price.toLocaleString()}đ</div></div><div class="d-flex justify-content-between align-items-center"><div class="btn-group btn-group-sm"><button class="btn btn-secondary" onclick="updateTempQty(${dish.dishID}, -1)">-</button><span class="btn btn-light border fw-bold" style="min-width:35px">${qty}</span><button class="btn btn-secondary" onclick="updateTempQty(${dish.dishID}, 1)">+</button></div><button class="btn btn-sm ${note ? 'btn-warning' : 'btn-outline-secondary'}" onclick="openNoteModal(${dish.dishID}, 'menu', '${note}')"><i class="fas fa-comment-dots"></i> ${note ? 'Sửa Note' : 'Ghi chú'}</button></div>${note ? `<div class="text-warning small fst-italic mt-1 ms-1"><i class="fas fa-pen"></i> ${note}</div>` : ''}</div>`;
            }
            dishList.appendChild(wrapper);
        });
    });
    updateMenuActionBar();
}

function updateTempQty(id, delta) {
    if (!appState.tempMenuSelection[id]) appState.tempMenuSelection[id] = { qty: 0, note: "" };
    appState.tempMenuSelection[id].qty += delta;
    if (appState.tempMenuSelection[id].qty <= 0) delete appState.tempMenuSelection[id];
    renderMenuUI();
}
function incrementDish(id) { updateTempQty(id, 1); }

// --- NOTE MODAL ---
let currentNoteTarget = null;
function openNoteModal(id, type, currentNote) { currentNoteTarget = { id, type }; document.getElementById('noteInput').value = currentNote || ""; new bootstrap.Modal(document.getElementById('noteModal')).show(); }
function saveNote() {
    const note = document.getElementById('noteInput').value.trim(); bootstrap.Modal.getInstance(document.getElementById('noteModal')).hide();
    if (currentNoteTarget.type === 'menu') { if (appState.tempMenuSelection[currentNoteTarget.id]) { appState.tempMenuSelection[currentNoteTarget.id].note = note; renderMenuUI(); } }
    else if (currentNoteTarget.type === 'cart') { updateCartItem(currentNoteTarget.id, null, note); } // null qty = keep current
}
// Nếu gọi updateCartItem với qty null thì phải lấy qty cũ -> Cần sửa lại hàm updateCartItem 1 chút
// Nhưng ở trên ta gọi API luôn. Để đơn giản, khi gọi từ Note modal, ta cần tìm item trong list để lấy qty
async function saveNote() {
    const note = document.getElementById('noteInput').value.trim(); bootstrap.Modal.getInstance(document.getElementById('noteModal')).hide();
    if (currentNoteTarget.type === 'menu') {
        if (appState.tempMenuSelection[currentNoteTarget.id]) { appState.tempMenuSelection[currentNoteTarget.id].note = note; renderMenuUI(); }
    } else if (currentNoteTarget.type === 'cart') {
        const item = appState.orderDetails.find(d => d.orderDetailID === currentNoteTarget.id);
        if (item) updateCartItem(item.orderDetailID, item.quantity, note);
    }
}

// --- SEARCH VIẾT TẮT ---
function searchMenu() {
    const term = removeAccents(document.getElementById('searchDish').value.toLowerCase());
    document.querySelectorAll('.dish-item').forEach(wrapper => {
        const nameEl = wrapper.querySelector('h6');
        if (nameEl) {
            const rawName = nameEl.innerText;
            const name = removeAccents(rawName.toLowerCase());
            const acronym = getAcronym(rawName);
            wrapper.style.display = (name.includes(term) || acronym.includes(term)) ? 'block' : 'none';
        }
    });
}

function updateMenuActionBar() {
    const bar = document.getElementById('menuActionBar'); let total = 0; for (let k in appState.tempMenuSelection) total += appState.tempMenuSelection[k].qty;
    if (total > 0) { bar.style.display = 'flex'; bar.style.setProperty('display', 'flex', 'important'); document.getElementById('selectedCount').innerText = total; } else { bar.style.display = 'none'; }
}

async function confirmMenuSelection() {
    const itemsToAdd = []; for (const [idStr, data] of Object.entries(appState.tempMenuSelection)) { itemsToAdd.push({ dishID: parseInt(idStr), quantity: data.qty, note: data.note || "" }); }
    if (itemsToAdd.length === 0) return;

    // Nếu chưa có đơn -> Create, ngược lại -> Add
    let url = appState.orderDetails.length === 0 ? `${API_URL}/Order/create` : `${API_URL}/Order/${appState.currentTableId}/add`;
    let payload = appState.orderDetails.length === 0 ? { tableID: appState.currentTableId, accID: currentUser.accID || 1, items: itemsToAdd } : { details: itemsToAdd };

    try { const res = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) }); if (res.ok) { showToast("Đã thêm vào giỏ"); appState.tempMenuSelection = {}; showView('view-detail'); document.querySelector('a[href="#tab-cart"]').click(); loadOrderData(appState.currentTableId); } else { showToast("Lỗi: " + await res.text(), 'danger'); } } catch (e) { showToast("Lỗi kết nối", 'danger'); }
}

async function sendOrderToKitchen() {
    const btn = document.querySelector('#cartActionBar button'); btn.disabled = true;
    try { const res = await fetch(`${API_URL}/Order/${appState.currentTableId}/send`, { method: 'POST' }); if (res.ok) { showToast('Đã gửi bếp thành công!'); document.querySelector('a[href="#tab-confirmed"]').click(); loadOrderData(appState.currentTableId); } else { showToast('Không có món mới để gửi', 'warning'); } } catch (e) { showToast('Lỗi kết nối!', 'danger'); } finally { btn.disabled = false; }
}

function cancelMenuSelection() { appState.tempMenuSelection = {}; showView('view-detail'); }
function logout() { localStorage.removeItem('posUser'); window.location.href = 'index.html'; }