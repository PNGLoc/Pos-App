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
    // 1. Cập nhật State bàn hiện tại
    appState.currentTableId = tableId;

    try {
        const res = await fetch(`${API_URL}/Order/${tableId}`);
        if (res.ok) {
            const data = await res.json();

            // 2. Lưu OrderID (Quan trọng cho thanh toán)
            appState.currentOrderId = data.orderID || data.OrderID || 0;

            // 3. Lấy danh sách món
            appState.orderDetails = data.Details || data.details || data.orderDetails || [];
        } else {
            // Bàn trống hoặc chưa có đơn
            appState.currentOrderId = 0;
            appState.orderDetails = [];
        }
    } catch (e) {
        console.error("Load order error:", e);
        appState.currentOrderId = 0;
        appState.orderDetails = [];
    }

    // 4. Tính tổng tiền món đã chốt
    const confirmedTotal = appState.orderDetails
        .filter(d => d.itemStatus !== 'New')
        .reduce((sum, d) => sum + d.totalAmount, 0);

    const totalEl = document.getElementById('confirmedTotal');
    if (totalEl) totalEl.innerText = confirmedTotal.toLocaleString() + 'đ';

    // 5. [QUAN TRỌNG] Cập nhật ẩn/hiện các nút chức năng theo quyền
    updateUIByPermission();

    renderCartTab();
    renderConfirmedTab();
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
        // Chỉ hiện nút hủy nếu trạng thái là 'Sent' (Đã gửi)
        let cancelBtn = '';

        if (d.itemStatus === 'Sent') {
            badge = 'bg-info text-dark';
            txt = 'Đã gửi';
            // Thêm nút Hủy (X)
            if (currentUser && currentUser.canCancelItem) {
                cancelBtn = `<button class="btn btn-sm btn-outline-danger ms-1" style="padding: 0px 8px;"onclick="openCancelModal(${d.orderDetailID}, ${d.quantity}, '${d.dishName}')"><i class="fas fa-times"></i></button>`;
            }
        }
        else if (d.itemStatus === 'Done') { badge = 'bg-success'; txt = 'Đã ra'; }

        // Chèn biến ${cancelBtn} vào cuối div
        container.innerHTML += `<div class="d-flex justify-content-between p-2 border-bottom"><div><span class="fw-bold">${d.dishName}</span> <br><small class="text-muted">${d.quantity} x ${(d.totalAmount / d.quantity).toLocaleString()}</small>${d.note ? `<br><small class="text-warning fst-italic">"${d.note}"</small>` : ''}</div><div class="text-end"><div class="fw-bold">${d.totalAmount.toLocaleString()}</div><span class="badge ${badge}">${txt}</span>${cancelBtn}</div></div>`;
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

// --- 1. Gửi yêu cầu In ---
async function requestBillMobile() {
    if (!confirm("Gửi yêu cầu in bill cho thu ngân?")) return;
    try {
        await fetch(`${API_URL}/Order/${appState.currentTableId}/request-payment`, { method: 'POST' });
        showToast("Đã gửi yêu cầu!");
    } catch (e) { showToast("Lỗi mạng", "danger"); }
}

// --- 2. Chuyển bàn ---
async function moveTableMobile() {
    // Check quyền client-side cho nhanh
    if (!currentUser.canMoveTable) { showToast("Bạn không có quyền chuyển bàn!", "warning"); return; }

    let targetId = prompt("Nhập số ID bàn muốn chuyển đến:");
    if (!targetId) return;

    try {
        const res = await fetch(`${API_URL}/Order/${appState.currentTableId}/move`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ accID: currentUser.accID, targetTableID: parseInt(targetId) })
        });
        const data = await res.json();
        if (res.ok) {
            showToast(data.Message);
            showView('view-tables'); // Quay về danh sách bàn
        } else {
            showToast(data.Message || await res.text(), "danger");
        }
    } catch (e) { showToast("Lỗi kết nối", "danger"); }
}

// --- 3. Thanh toán ---
async function doPaymentMobile() {
    if (!currentUser.canPayment) { showToast("Bạn không có quyền thanh toán!", "warning"); return; }

    // Kiểm tra xem có OrderID hợp lệ không
    if (!appState.currentOrderId || appState.currentOrderId === 0) {
        showToast("Bàn này đang trống hoặc chưa có đơn hàng!", "warning");
        return;
    }

    if (!confirm("Xác nhận thanh toán và in hóa đơn?")) return;

    try {
        // Log ra console để kiểm tra dữ liệu trước khi gửi (F12 trên trình duyệt để xem)
        const payload = {
            accID: currentUser.accID,
            orderID: appState.currentOrderId, // Phải đảm bảo cái này có giá trị số (VD: 105)
            paymentMethod: "Cash",
            discountPercent: 0,
            discountAmount: 0
        };
        console.log("Sending payment:", payload);

        const res = await fetch(`${API_URL}/Order/checkout-mobile`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (res.ok) {
            showToast("Thanh toán thành công!");
            // Reset lại trạng thái
            appState.currentOrderId = 0;
            appState.orderDetails = [];
            renderConfirmedTab();
            // Quay về trang chủ
            showView('view-tables');
        } else {
            // Đọc lỗi từ server trả về
            const errorText = await res.text();
            showToast("Lỗi: " + errorText, "danger");
        }
    } catch (e) {
        console.error(e);
        showToast("Lỗi kết nối", "danger");
    }
}
async function cancelItemMobile(detailId, maxQty) {
    // 1. Check quyền
    if (!currentUser || !currentUser.canCancelItem) {
        showToast("Bạn không có quyền hủy món!", "warning");
        return;
    }

    // 2. Hỏi số lượng muốn hủy
    let qty = prompt(`Nhập số lượng hủy (Tối đa ${maxQty}):`, 1);
    if (!qty) return;

    qty = parseInt(qty);
    if (isNaN(qty) || qty <= 0 || qty > maxQty) {
        showToast("Số lượng không hợp lệ", "warning");
        return;
    }

    // 3. Gọi API (Lý do mặc định là "Hủy từ Mobile")
    try {
        const res = await fetch(`${API_URL}/Order/cancel-item`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                accID: currentUser.accID,
                orderDetailID: detailId,
                quantity: qty,
                reason: "Hủy từ Mobile" // <--- Hardcode lý do tại đây
            })
        });

        if (res.ok) {
            showToast(`Đã hủy ${qty} món thành công`);
            loadOrderData(appState.currentTableId); // Reload lại dữ liệu
        } else {
            showToast(await res.text(), "danger");
        }
    } catch (e) {
        showToast("Lỗi kết nối server", "danger");
    }
}
// Thêm hàm này vào cuối file hoặc chỗ nào tiện quản lý
function updateUIByPermission() {
    if (!currentUser) return;

    // Kiểm tra xem bàn có đơn hàng không (Có OrderID và trạng thái không phải Empty)
    // Lưu ý: appState.currentOrderId được lấy trong hàm loadOrderData
    const hasOrder = appState.currentOrderId && appState.currentOrderId > 0;

    // 1. Nút Chuyển bàn (Luôn hiện nếu có quyền, hoặc chỉ hiện khi có đơn - tuỳ bạn)
    const btnMove = document.getElementById('btnMoveTable');
    if (btnMove) {
        // Logic: Phải có quyền VÀ bàn đang có khách mới chuyển được
        btnMove.style.display = (currentUser.canMoveTable && hasOrder) ? 'block' : 'none';
    }

    // 2. Nút Thanh toán (Chỉ hiện khi có Khách + Có Quyền)
    const btnPay = document.getElementById('btnPayment'); // hoặc id='btnCheckoutMobile'
    if (btnPay) {
        if (currentUser.canPayment && hasOrder) {
            btnPay.style.display = 'block';
        } else {
            btnPay.style.display = 'none';
        }
    }

    // 3. Nút Yêu cầu In/Thanh toán (Chỉ hiện khi có Khách)
    const btnRequest = document.getElementById('btnRequestBill');
    if (btnRequest) {
        btnRequest.style.display = hasOrder ? 'block' : 'none';
    }
}

// --- BIẾN TOÀN CỤC MỚI ---
let currentActionType = '';
let cancelState = { detailId: 0, currentQty: 1, maxQty: 0 };
let moveTarget = null;

// --- 1. XỬ LÝ MENU TRƯỢT ---
function toggleActionMenu() {
    const sheet = document.getElementById('actionSheet');
    const overlay = document.getElementById('actionSheetOverlay');

    if (sheet.classList.contains('show')) {
        sheet.classList.remove('show');
        overlay.style.display = 'none';
    } else {
        // Cập nhật quyền trước khi hiện menu
        updateMenuPermissions();
        sheet.classList.add('show');
        overlay.style.display = 'block';
    }
}

function updateMenuPermissions() {
    // Chỉ hiện nút nếu CÓ ĐƠN và CÓ QUYỀN
    const hasOrder = appState.currentOrderId && appState.currentOrderId > 0;

    // 1. Yêu cầu In (Ai cũng được dùng nếu có đơn)
    document.getElementById('btnMenuRequest').style.display = hasOrder ? 'flex' : 'none';

    // 2. Chuyển bàn (Cần quyền + Có đơn)
    const canMove = currentUser && currentUser.canMoveTable && hasOrder;
    document.getElementById('btnMenuMove').style.display = canMove ? 'flex' : 'none';

    // 3. Thanh toán (Cần quyền + Có đơn)
    const canPay = currentUser && currentUser.canPayment && hasOrder;
    document.getElementById('btnMenuPay').style.display = canPay ? 'flex' : 'none';
}

// --- 2. XỬ LÝ POPUP XÁC NHẬN (IN & THANH TOÁN) ---
function openConfirmModal(type) {
    if (type !== 'move') toggleActionMenu(); // Đóng menu nếu không phải luồng chuyển bàn
    currentActionType = type;

    const title = document.getElementById('confirmTitle');
    const msg = document.getElementById('confirmMessage');
    const btn = document.getElementById('btnConfirmAction');
    const icon = document.getElementById('confirmIcon');

    if (type === 'request') {
        title.innerText = "Yêu cầu In";
        msg.innerText = "Gửi yêu cầu in bill / thanh toán cho thu ngân?";
        btn.className = "btn btn-warning text-dark";
        icon.className = "fas fa-print fa-3x text-warning";
        btn.onclick = executeRequestBill;
    } else if (type === 'payment') {
        title.innerText = "Thanh toán";
        msg.innerText = "Xác nhận thanh toán đơn hàng này ngay?";
        btn.className = "btn btn-success";
        icon.className = "fas fa-money-bill-wave fa-3x text-success";
        btn.onclick = executePayment;
    }
    else if (type === 'move') {
        title.innerText = "Xác nhận Chuyển bàn";

        // Thông báo đơn giản, gộp chung ý nghĩa
        const statusText = moveTarget.tableStatus !== 'Empty' ? "(Đang có khách)" : "";
        msg.innerHTML = `Chuyển đơn sang bàn <b>${moveTarget.tableName}</b> ${statusText}?`;

        btn.className = "btn btn-primary";
        icon.className = "fas fa-exchange-alt fa-3x text-primary";
        btn.onclick = executeMoveTableAction; // Gọi hàm thực thi mới
    }

    document.getElementById('confirmModal').style.display = 'flex';
}

// Logic thực thi (API call)
async function executeRequestBill() {
    closeModal('confirmModal');
    try {
        await fetch(`${API_URL}/Order/${appState.currentTableId}/request-payment`, { method: 'POST' });
        showToast("Đã gửi yêu cầu!");
    } catch (e) { showToast("Lỗi kết nối", "danger"); }
}

async function executePayment() {
    closeModal('confirmModal');
    // Payload thanh toán
    const payload = {
        accID: currentUser.accID,
        orderID: appState.currentOrderId,
        paymentMethod: "Cash", discountPercent: 0, discountAmount: 0
    };
    try {
        const res = await fetch(`${API_URL}/Order/checkout-mobile`, {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload)
        });
        if (res.ok) {
            showToast("Thanh toán thành công!");
            showView('view-tables');
        } else {
            showToast(await res.text(), "danger");
        }
    } catch (e) { showToast("Lỗi kết nối", "danger"); }
}

// --- 3. XỬ LÝ CHUYỂN BÀN (HIỆN DANH SÁCH) ---
async function openMoveTableModal() {
    toggleActionMenu();
    document.getElementById('moveTableModal').style.display = 'flex';

    const grid = document.getElementById('tableSelectGrid');
    grid.innerHTML = '<div class="text-center w-100">Đang tải...</div>';

    try {
        const res = await fetch(`${API_URL}/Table`); // Lấy danh sách bàn
        const tables = await res.json();

        grid.innerHTML = '';
        tables.forEach(t => {
            if (t.tableID === appState.currentTableId) return; // Bỏ qua bàn hiện tại

            const div = document.createElement('div');
            // Style khác nhau nếu bàn có khách hay trống
            const isOccupied = t.tableStatus !== 'Empty';
            div.className = `table-option ${isOccupied ? 'bg-warning-subtle border-warning' : ''}`;

            div.innerHTML = `
                <div class="fw-bold">${t.tableName}</div>
                <small class="${isOccupied ? 'text-danger' : 'text-success'}">
                    ${isOccupied ? 'Gộp bàn' : 'Trống'}
                </small>
            `;
            div.onclick = () => prepareMoveTable(t);
            grid.appendChild(div);
        });
    } catch (e) { grid.innerHTML = 'Lỗi tải danh sách'; }
}
function prepareMoveTable(targetTable) {
    // 1. Lưu bàn đích vào biến tạm
    moveTarget = targetTable;

    // 2. Đóng danh sách chọn bàn
    closeModal('moveTableModal');

    // 3. Mở Popup xác nhận (Dùng chung cái confirmModal)
    openConfirmModal('move');
}
// --- HÀM THỰC THI CHUYỂN BÀN (GỌI API) ---
async function executeMoveTableAction() {
    closeModal('confirmModal'); // Tắt popup

    if (!moveTarget) return;

    try {
        const res = await fetch(`${API_URL}/Order/${appState.currentTableId}/move`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                accID: currentUser.accID,
                targetTableID: moveTarget.tableID
            })
        });

        if (res.ok) {
            showToast("Chuyển bàn thành công!");
            showView('view-tables'); // Quay về trang chủ
        } else {
            showToast(await res.text(), "danger");
        }
    } catch (e) {
        showToast("Lỗi kết nối", "danger");
    }
}

// --- 4. XỬ LÝ HỦY MÓN (+/-) ---
// Hàm này được gọi từ nút (X) trong danh sách món
function openCancelModal(detailId, maxQty, dishName) {
    if (!currentUser.canCancelItem) { showToast("Không có quyền hủy!", "warning"); return; }

    cancelState = { detailId: detailId, currentQty: 1, maxQty: maxQty };

    document.getElementById('cancelItemName').innerText = dishName;
    document.getElementById('cancelMaxQty').innerText = maxQty;
    document.getElementById('cancelQtyDisplay').innerText = "1";

    document.getElementById('cancelModal').style.display = 'flex';
}

function adjustCancelQty(delta) {
    let newQty = cancelState.currentQty + delta;
    if (newQty < 1) newQty = 1;
    if (newQty > cancelState.maxQty) newQty = cancelState.maxQty;

    cancelState.currentQty = newQty;
    document.getElementById('cancelQtyDisplay').innerText = newQty;
}

async function submitCancelItem() {
    closeModal('cancelModal');
    try {
        const res = await fetch(`${API_URL}/Order/cancel-item`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                accID: currentUser.accID,
                orderDetailID: cancelState.detailId,
                quantity: cancelState.currentQty,
                reason: "Mobile Cancel"
            })
        });

        if (res.ok) {
            showToast(`Đã hủy ${cancelState.currentQty} món`);
            loadOrderData(appState.currentTableId);
        } else { showToast(await res.text(), "danger"); }
    } catch (e) { showToast("Lỗi kết nối", "danger"); }
}

// --- UTILS ---
function closeModal(id) {
    document.getElementById(id).style.display = 'none';
}