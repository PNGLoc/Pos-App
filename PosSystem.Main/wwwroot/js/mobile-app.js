const API_URL = '/api';

let currentUser = null;
let appState = {
    tables: [],
    categories: [], // Menu Categories
    tableCategories: [], // Table Categories from DB
    currentTableId: null,
    orderDetails: [], // Dữ liệu từ server
    tempMenuSelection: {}, // Lưu tạm món đang chọn: { dishID: { qty: 1, note: '' } }
    currentFilter: 'All',
    currentMenuCategory: 'All' // [NEW] Filter cho Menu
};

let suppressPopstate = false;
const BACK_STACK_DEPTH = 4;

document.addEventListener('DOMContentLoaded', async () => {
    const userStr = localStorage.getItem('posUser');
    if (!userStr) { window.location.href = 'index.html'; return; }
    currentUser = JSON.parse(userStr);

    initBackNavigation();

    updateDetailTabsOffset();
    window.addEventListener('resize', updateDetailTabsOffset);

    await loadTables();
    await loadMenuData();
    setTimeout(() => initSignalR(), 500);

    // [NEW] Hiển thị thông tin user lên Menu
    if (currentUser) {
        document.getElementById('menuUserName').innerText = currentUser.accName || "Unknown";
        document.getElementById('menuUserRole').innerText = currentUser.accRole || "Staff";
        // Lấy chữ cái đầu làm Avatar
        const firstLetter = (currentUser.accName || "U").charAt(0).toUpperCase();
        document.getElementById('menuUserAvatar').innerText = firstLetter;
    }
});

function initBackNavigation() {
    // Create a lock state so back gesture won't exit to login
    history.replaceState({ view: 'view-tables', lock: true }, '', '#tables');
    pushLockStates(BACK_STACK_DEPTH);

    window.addEventListener('popstate', () => {
        if (suppressPopstate) { suppressPopstate = false; return; }

        if (closeAnyOpenModal()) return;
        if (isActionMenuOpen()) { closeActionMenu(false); return; }

        const active = getActiveViewId();
        if (active === 'view-menu') {
            showView('view-detail', { push: false });
            return;
        }

        if (active === 'view-detail') {
            showView('view-tables', { push: false });
            return;
        }

        // Stay on tables view, block leaving to login
        pushLockStates(1);
    });
}

function pushLockStates(count = 1) {
    for (let i = 0; i < count; i++) {
        history.pushState({ view: 'view-tables', lock: true }, '', '#tables');
    }
}

function getActiveViewId() {
    const active = document.querySelector('.view-section.active');
    return active ? active.id : null;
}

function isActionMenuOpen() {
    const sheet = document.getElementById('actionSheet');
    return !!(sheet && sheet.classList.contains('show'));
}

function closeActionMenu(syncHistory = true) {
    const sheet = document.getElementById('actionSheet');
    const overlay = document.getElementById('actionSheetOverlay');
    if (!sheet || !overlay) return;
    sheet.classList.remove('show');
    overlay.style.display = 'none';

    if (syncHistory && history.state && history.state.popup === 'actionSheet') {
        suppressPopstate = true;
        history.back();
    }
}

function closeAnyOpenModal() {
    const modalIds = ['confirmModal', 'moveTableModal', 'cancelModal', 'paymentModal'];
    for (const id of modalIds) {
        const el = document.getElementById(id);
        if (el && el.style.display === 'flex') {
            closeModal(id);
            return true;
        }
    }

    const noteEl = document.getElementById('noteModal');
    if (noteEl && noteEl.classList.contains('show')) {
        const instance = bootstrap.Modal.getInstance(noteEl);
        if (instance) instance.hide();
        return true;
    }

    return false;
}

function updateDetailTabsOffset() {
    const header = document.getElementById('detailHeader');
    if (!header) return;
    const h = Math.ceil(header.getBoundingClientRect().height);
    document.documentElement.style.setProperty('--detail-header-height', `${h}px`);
}

// --- SIDEBAR TOGGLE ---
function toggleSidebar() {
    const drawer = document.getElementById('sidebarDrawer');
    const overlay = document.getElementById('sidebarOverlay');
    if (!drawer || !overlay) return;

    if (drawer.classList.contains('show')) {
        drawer.classList.remove('show');
        overlay.classList.remove('show');
        setTimeout(() => overlay.style.display = 'none', 300);
    } else {
        overlay.style.display = 'block';
        // force reflow
        overlay.offsetHeight;
        overlay.classList.add('show');
        drawer.classList.add('show');
    }
}

function initSignalR() {
    if (typeof signalR === 'undefined') return;

    // 1. Cấu hình Connection
    // Ưu tiên WebSockets để realtime ổn định hơn.
    // Một số mạng/thiết bị có thể chặn WS -> fallback sang negotiate mặc định.
    function createConnection(forceWebSockets) {
        const urlOptions = forceWebSockets
            ? { transport: signalR.HttpTransportType.WebSockets, skipNegotiation: true }
            : undefined;

        return new signalR.HubConnectionBuilder()
            .withUrl("/posHub", urlOptions)
            .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
            .configureLogging(signalR.LogLevel.Warning)
            .build();
    }

    let connection = createConnection(true);

    // --- TIMEOUT (ANTI-FALSE-DISCONNECT) ---
    // 6s là quá nhạy: chỉ cần browser pause/GC/CPU spike ngắn là bị "Server timeout elapsed".
    // Đặt ngưỡng thoải mái hơn để dùng WiFi ổn định không bị reconnect giả.
    connection.keepAliveIntervalInMilliseconds = 15000;   // ping định kỳ từ client
    connection.serverTimeoutInMilliseconds = 60000;       // nếu 60s không nhận gì từ server mới coi là timeout
    // --------------------------------------

    // --- CÁC HÀM XỬ LÝ GIAO DIỆN ---
    const overlay = document.getElementById('connectionOverlay');
    const title = document.getElementById('connectionTitle');
    const msg = document.getElementById('connectionMessage');
    const spinner = document.getElementById('connectionSpinner');
    const iconErr = document.getElementById('connectionIconErr');
    const btnReload = document.getElementById('btnReload');

    function updateConnectionStatus(isConnected) {
        const el = document.getElementById('menuConnectionStatus');
        if (!el) return;

        if (isConnected) {
            el.innerHTML = '<i class="fas fa-circle" style="font-size: 8px;"></i> Kết nối ổn định';
            el.classList.remove('error');
        } else {
            el.innerHTML = '<i class="fas fa-exclamation-circle"></i> Mất kết nối!';
            el.classList.add('error');
        }
    }

    function showOverlay(tit, message, isFatal = false) {
        // [NEW] Cập nhật trạng thái menu
        updateConnectionStatus(false);
        if (!overlay) return;
        overlay.classList.remove('d-none');
        title.innerText = tit;
        msg.innerText = message;

        if (isFatal) {
            spinner.classList.add('d-none');
            iconErr.classList.remove('d-none');
            btnReload.classList.remove('d-none');
        } else {
            spinner.classList.remove('d-none');
            iconErr.classList.add('d-none');
            btnReload.classList.add('d-none');
        }
    }

    function hideOverlay() {
        if (overlay) overlay.classList.add('d-none');
    }

    // --- SỰ KIỆN SIGNALR ---

    // [ANTI-FLICKER] Mobile browser/Wifi có thể làm websocket "rụng" rất ngắn (vài trăm ms)
    // dù nhìn vẫn ổn định. Ta debounce để không spam overlay/toast và tránh reload dữ liệu liên tục.
    let reconnectingSince = null;
    let reconnectOverlayShown = false;
    let reconnectOverlayTimer = null;
    let hadFatalDisconnect = false;
    const RECONNECT_OVERLAY_DELAY_MS = 2500; // chỉ hiện overlay nếu mất kết nối đủ lâu ("mất thật")

    function bindSignalREvents(conn) {
        // 1. Đang thử kết nối lại (Mạng chập chờn hoặc Server vừa tắt)
        conn.onreconnecting(error => {
            console.warn('Kết nối không ổn định:', error);

            // Bình thường chỉ cần icon trạng thái
            updateConnectionStatus(false);

            if (!reconnectingSince) reconnectingSince = Date.now();
            reconnectOverlayShown = false;

            if (reconnectOverlayTimer) clearTimeout(reconnectOverlayTimer);
            reconnectOverlayTimer = setTimeout(() => {
                // Nếu vẫn đang reconnect sau delay thì mới show overlay
                reconnectOverlayShown = true;
                showOverlay('Mất kết nối!', 'Đang cố gắng tìm máy chủ...', false);
            }, RECONNECT_OVERLAY_DELAY_MS);
        });

        // 2. Đã kết nối lại thành công
        conn.onreconnected(connectionId => {
            console.log('Đã kết nối lại:', connectionId);

            if (reconnectOverlayTimer) { clearTimeout(reconnectOverlayTimer); reconnectOverlayTimer = null; }

            const since = reconnectingSince;
            reconnectingSince = null;

            // Nếu overlay đã hiện thì chắc chắn phải ẩn
            if (reconnectOverlayShown) hideOverlay();
            updateConnectionStatus(true);

            // Chỉ toast khi vừa mất kết nối "thật" (overlay đã hiện) hoặc từng bị onclose (fatal)
            if (reconnectOverlayShown || hadFatalDisconnect) {
                showToast('Đã khôi phục kết nối!', 'success');

                // Sau khi mất thật, refresh để đảm bảo đồng bộ
                loadTables(false);
                if (appState.currentTableId) loadOrderData(appState.currentTableId);
            }

            hadFatalDisconnect = false;

            reconnectOverlayShown = false;
        });

        // 3. Mất kết nối hoàn toàn (Hết số lần thử hoặc lỗi nghiêm trọng)
        conn.onclose(error => {
            console.error('Ngắt kết nối hẳn:', error);
            hadFatalDisconnect = true;
            showOverlay('Không tìm thấy máy chủ', 'Vui lòng kiểm tra lại Wifi hoặc Máy tính thu ngân.', true);
        });

        // --- LOGIC NGHIỆP VỤ ---
        conn.on("TableUpdated", (tableId) => {
            loadTables(false);
            if (appState.currentTableId == tableId) loadOrderData(tableId);
        });
    }

    bindSignalREvents(connection);

    // Bắt đầu kết nối
    async function start() {
        try {
            await connection.start();
            console.log("SignalR Connected.");
            hideOverlay(); // Ẩn overlay nếu đang hiện
            updateConnectionStatus(true);
        } catch (err) {
            console.error("Khởi động lỗi:", err);

            // Nếu WebSockets bị chặn/không hỗ trợ -> fallback sang negotiate mặc định
            const msg = (err && (err.message || err.toString())) || '';
            const isWebSocketStartFail = msg.toLowerCase().includes('websocket') || msg.toLowerCase().includes('negotiation') || msg.toLowerCase().includes('transport');
            if (isWebSocketStartFail) {
                try {
                    console.warn('WebSocket failed, falling back to default transports...');
                    connection = createConnection(false);
                    // Apply same timeout settings
                    connection.keepAliveIntervalInMilliseconds = 15000;
                    connection.serverTimeoutInMilliseconds = 60000;
                    bindSignalREvents(connection);
                    await connection.start();
                    console.log("SignalR Connected (fallback).");
                    hideOverlay();
                    updateConnectionStatus(true);
                    return;
                } catch (e2) {
                    console.error('Fallback start failed:', e2);
                }
            }

            // Nếu mở app lên mà không thấy server ngay -> Báo lỗi luôn
            showOverlay('Không thể kết nối', 'Đang thử lại sau 5 giây...', false);
            setTimeout(start, 5000);
        }
    }

    start();
}

// --- TIỆN ÍCH SEARCH ---
function removeAccents(str) {
    if (!str) return "";
    return str.normalize("NFD")
        .replace(/[\u0300-\u036f]/g, "")
        .replace(/đ/g, "d").replace(/Đ/g, "D");
}
function getAcronym(str) { return removeAccents(str).toLowerCase().split(/\s+/).map(w => w[0]).join(''); }

// --- NAV & UI ---
function showView(viewId, options = { push: true }) {
    document.querySelectorAll('.view-section').forEach(el => el.classList.remove('active'));
    document.getElementById(viewId).classList.add('active');
    if (viewId === 'view-detail') {
        requestAnimationFrame(() => requestAnimationFrame(updateDetailTabsOffset));
    }

    if (options.push) {
        history.pushState({ view: viewId, tableId: appState.currentTableId }, '', `#${viewId.replace('view-', '')}`);
    }
}
function showToast(msg, type = 'success') {
    const toastEl = document.getElementById('liveToast');
    if (toastEl) {
        document.getElementById('toastMessage').innerText = msg;
        toastEl.className = `toast align-items-center text-white bg-${type} border-0`;
        new bootstrap.Toast(toastEl, { delay: 1500 }).show();
    }
}

// --- LOGIC BÀN ---
// --- LOGIC BÀN ---
async function loadTables(renderFilter = true) {
    try {
        // Load Table Categories
        const catRes = await fetch(`${API_URL}/TableCategory`);
        if (catRes.ok) appState.tableCategories = await catRes.json();

        // Load Tables (Cache-busting)
        const res = await fetch(`${API_URL}/Table?t=${new Date().getTime()}`);
        appState.tables = await res.json();

        // [NEW] Update Detail Badge if we are currently viewing a table
        if (appState.currentTableId) {
            const currentTable = appState.tables.find(t => t.tableID === appState.currentTableId);
            if (currentTable) {
                const badge = document.getElementById('detailTableStatus');
                if (badge) {
                    // [MODIFIED] Logic Status & Color (Detail Badge)
                    const hasNew = currentTable.hasNewItems || currentTable.HasNewItems;
                    let statusText = 'Bàn trống';
                    let statusClass = 'text-dark fw-bold'; // Mặc định đen

                    if (hasNew) {
                        statusText = 'Đang gọi món';
                        statusClass = 'text-warning fw-bold';
                    } else if (currentTable.tableStatus === 'Occupied') {
                        statusText = 'Đã gọi món';
                        statusClass = 'text-success fw-bold';
                    }

                    badge.innerText = statusText;
                    badge.className = statusClass; // Bỏ class 'badge'
                }
            }
        }

        if (renderFilter) renderFilterButtons();
        renderTables(appState.currentFilter);
        startTableTimers(); // [NEW] Start timer loop
    } catch (e) { console.error(e); }
}

let tableTimerInterval = null;
function startTableTimers() {
    if (tableTimerInterval) clearInterval(tableTimerInterval);
    updateTableTimers(); // Run immediately
    tableTimerInterval = setInterval(updateTableTimers, 1000);
}

function updateTableTimers() {
    // Iterate over all table cards that have data-ordertime
    document.querySelectorAll('.table-timer').forEach(el => {
        const timeStr = el.dataset.ordertime;
        if (!timeStr) return;

        const startTime = new Date(timeStr);
        const now = new Date();
        const diff = Math.floor((now - startTime) / 1000); // seconds

        if (diff < 0) {
            el.innerText = "0p";
            return;
        }

        let display = "";
        // [MODIFIED] Show minutes only (User request)
        if (diff < 60) {
            display = "0p";
        } else if (diff < 3600) {
            const m = Math.floor(diff / 60);
            display = `${m}p`;
        } else {
            const h = Math.floor(diff / 3600);
            const m = Math.floor((diff % 3600) / 60);
            display = `${h}giờ ${m}p`;
        }
        el.innerText = display;
    });
}

function renderTables(filterId) {
    const grid = document.getElementById('tableGrid'); grid.innerHTML = '';

    // Filter logic: Check categoryID
    const filtered = (filterId === 'All' || filterId === null)
        ? appState.tables
        : appState.tables.filter(t => t.categoryID === filterId);

    document.querySelectorAll('#tableFilters .filter-btn').forEach(b => b.classList.remove('active'));

    // Update active button
    const btnSelector = filterId === 'All' ? 'btn-all' : `btn-cat-${filterId}`;
    const activeBtn = document.getElementById(btnSelector);
    if (activeBtn) activeBtn.classList.add('active');

    if (filtered.length === 0) {
        grid.innerHTML = '<div class="text-center w-100 text-muted mt-3">Không tìm thấy bàn nào</div>';
        return;
    }

    // [FIXED] Translate Labels
    filtered.forEach(t => {
        // [NEW] Logic Status & Color
        let cardClass = '';
        let statusText = 'Bàn trống';
        let statusTextClass = 'text-success';
        const hasNew = t.hasNewItems || t.HasNewItems;

        if (hasNew) {
            // Có món chưa gửi -> Đang gọi món (Vàng)
            cardClass = 'ordering';
            statusText = 'Đang gọi món';
            statusTextClass = 'text-warning'; // Hoặc text-dark
        } else if (t.tableStatus === 'Occupied') {
            // Đã gọi món (đã gửi) -> Xanh
            cardClass = 'occupied';
            statusText = 'Đã gọi món';
            statusTextClass = 'text-success';
        }

        const div = document.createElement('div');
        div.className = `table-card ${cardClass}`;

        // [NEW] Marker for Provisional Bill
        const provMarker = (t.hasProvisionalBill || t.HasProvisionalBill)
            ? `<div class="position-absolute end-0 m-1 text-primary" style="top: 22px; right: 5px;"><i class="fas fa-print bg-white rounded-circle p-1 border"></i></div>`
            : '';

        // [NEW] Marker for Request Payment
        const payMarker = (t.isRequestingPayment || t.IsRequestingPayment)
            ? `<div class="position-absolute start-0 m-1 text-danger" style="top: 22px; left: 5px;"><i class="fas fa-bell bg-white rounded-circle p-1 border"></i></div>`
            : '';

        div.onclick = () => openTableDetail(t);

        // [MODIFIED] Timer Element (Removed Border)
        // Chỉ hiện Timer nếu là Occupied hoặc Ordering (nếu muốn)
        // User yêu cầu "bỏ khung viền". GIữ text-danger để nổi bật.
        const showTimer = (t.tableStatus === 'Occupied' || hasNew) && t.orderTime;
        const timerHtml = showTimer
            ? `<div class="position-absolute top-0 start-50 translate-middle-x mt-0 text-danger fw-bold table-timer" data-ordertime="${t.orderTime}" style="z-index: 5; font-size: 0.85rem;"><i class="fas fa-clock me-1"></i> ...</div>`
            : '';

        const iconClass = t.categoryIconClass || t.CategoryIconClass || 'fas fa-chair';

        div.innerHTML = `
            ${provMarker}
            ${payMarker}
            ${timerHtml}
            <div class="fs-4 mb-1"><i class="${iconClass}"></i></div>
            <div class="fw-bold">${t.tableName}</div>
            <small class="${statusTextClass} fw-bold">
                ${statusText}
            </small>
        `;
        grid.appendChild(div);
    });
}

function renderFilterButtons() {
    const filterContainer = document.getElementById('tableFilters'); if (!filterContainer) return; filterContainer.innerHTML = '';

    // All Button
    const btnAll = document.createElement('button');
    btnAll.className = `filter-btn active`;
    btnAll.innerText = 'Tất cả';
    btnAll.id = 'btn-all';
    btnAll.onclick = () => filterTables('All');
    filterContainer.appendChild(btnAll);

    // Dynamic Buttons from API
    if (appState.tableCategories && appState.tableCategories.length > 0) {
        appState.tableCategories.forEach(cat => {
            const btn = document.createElement('button');
            btn.className = `filter-btn`;
            btn.innerText = cat.categoryName;
            btn.id = `btn-cat-${cat.categoryID}`;
            btn.onclick = () => filterTables(cat.categoryID);
            filterContainer.appendChild(btn);
        });
    } else {
        // Fallback if no categories (use old logic or just Show All)
        // Or if we want to show based on existing tableTypes as backup?
        // Let's stick to API Categories. If empty, user only sees "All".
    }
}

function filterTables(type) { appState.currentFilter = type; renderTables(type); }

function openTableDetail(table) {
    appState.currentTableId = table.tableID;
    document.getElementById('detailTableName').innerText = table.tableName;
    // [MODIFIED] Translate badge (Detail View)
    const hasNew = table.hasNewItems || table.HasNewItems;
    let statusText = 'Bàn trống';
    let statusClass = 'text-dark fw-bold';

    if (hasNew) {
        statusText = 'Đang gọi món';
        statusClass = 'text-warning fw-bold';
    } else if (table.tableStatus === 'Occupied') {
        statusText = 'Đã gọi món';
        statusClass = 'text-success fw-bold';
    }

    const badge = document.getElementById('detailTableStatus');
    badge.innerText = statusText;
    badge.className = statusClass;

    loadOrderData(table.tableID);
    showView('view-detail');
}

// --- LOGIC ORDER (CART & CONFIRMED) ---
async function loadOrderData(tableId) {
    // 1. Cập nhật State bàn hiện tại
    appState.currentTableId = tableId;

    try {
        const res = await fetch(`${API_URL}/Order/${tableId}?t=${new Date().getTime()}`);
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

    // 6. [NEW] Cập nhật Badge trên Tab
    updateTabBadges();

    renderCartTab();
    renderConfirmedTab();
}

function updateTabBadges() {
    const cartQty = appState.orderDetails
        .filter(d => d.itemStatus === 'New')
        .reduce((sum, d) => sum + d.quantity, 0);

    const confirmedQty = appState.orderDetails
        .filter(d => d.itemStatus !== 'New')
        .reduce((sum, d) => sum + d.quantity, 0);

    const badgeCart = document.getElementById('badgeCart');
    if (badgeCart) {
        badgeCart.innerText = cartQty;
        if (cartQty > 0) badgeCart.classList.remove('d-none');
        else badgeCart.classList.add('d-none');
    }

    const badgeConf = document.getElementById('badgeConfirmed');
    if (badgeConf) {
        badgeConf.innerText = confirmedQty;
        if (confirmedQty > 0) badgeConf.classList.remove('d-none');
        else badgeConf.classList.add('d-none');
    }
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
                    <button class="btn btn-sm btn-outline-danger" onclick="confirmDeleteCartItem(${item.orderDetailID})"><i class="fas fa-trash"></i></button>
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
            body: JSON.stringify({ accID: (currentUser && currentUser.accID) ? currentUser.accID : 0, orderDetailID: detailId, quantity: newQty, note: note })
        });
        if (res.ok) loadOrderData(appState.currentTableId);
        else showToast(await res.text(), 'danger');
    } catch (e) { showToast("Lỗi kết nối", 'danger'); }
}

// --- CART: CONFIRM DELETE ---
let pendingCartDeleteDetailId = 0;

function confirmDeleteCartItem(detailId) {
    pendingCartDeleteDetailId = detailId;

    const item = appState.orderDetails.find(d => d.orderDetailID === detailId);
    const payload = item ? { dishName: item.dishName, qty: item.quantity } : null;
    openConfirmModal('deleteCart', payload);
}

async function executeDeleteCartItem() {
    const id = pendingCartDeleteDetailId;
    pendingCartDeleteDetailId = 0;
    closeModal('confirmModal');
    if (!id) return;

    await updateCartItem(id, 0, '');
}

// --- TAB CONFIRMED ---
function renderConfirmedTab() {
    const container = document.getElementById('confirmedList'); container.innerHTML = '';
    const items = appState.orderDetails.filter(d => d.itemStatus !== 'New');
    if (items.length === 0) { container.innerHTML = `<div class="text-center text-muted mt-5">Chưa có món nào được gọi</div>`; return; }

    // [FIXED] Group by Dish + Note + Status + DiscountRate (Smart Cancel)
    const grouped = [];
    items.forEach(item => {
        const rateKey = Number(item.discountRate || 0).toFixed(4);
        const key = `${item.dishID}_${(item.note || "").trim()}_${item.itemStatus}_${rateKey}`;
        const exist = grouped.find(g => {
            const gRate = Number(g.discountRate || 0).toFixed(4);
            return `${g.dishID}_${(g.note || "").trim()}_${g.itemStatus}_${gRate}` === key;
        });
        if (exist) {
            exist.quantity += item.quantity;
            exist.totalAmount += item.totalAmount;
            // Gom các ID con vào mảng để xử lý hủy sau này
            if (!exist.subIds) exist.subIds = [item.orderDetailID];
            else exist.subIds.push(item.orderDetailID);
        } else {
            // Clone object để không ảnh hưởng dữ liệu gốc
            let clone = { ...item };
            clone.subIds = [item.orderDetailID];
            grouped.push(clone);
        }
    });

    grouped.forEach(d => {
        let badge = 'bg-secondary', txt = d.itemStatus;
        // Chỉ hiện nút hủy nếu trạng thái là 'Sent' (Đã gửi)
        let cancelBtn = '';

        if (d.itemStatus === 'Sent') {
            badge = 'bg-info text-dark';
            txt = 'Đã gửi';
            // Thêm nút Hủy (X) - Truyền key định danh nhóm
            if (currentUser && currentUser.canCancelItem) {
                // Encode key để truyền vào hàm safely
                const groupKey = `${d.dishID}|${(d.note || "").trim()}|${d.itemStatus}|${Number(d.discountRate || 0).toFixed(4)}`;
                cancelBtn = `<button class="btn btn-sm btn-outline-danger ms-1" style="padding: 0px 8px;" onclick="openCancelModalGroup('${groupKey}', ${d.quantity}, '${d.dishName}')"><i class="fas fa-times"></i></button>`;
            }
        }
        else if (d.itemStatus === 'Done') { badge = 'bg-success'; txt = 'Đã ra'; }

        // Chèn biến ${cancelBtn} vào cuối div
        container.innerHTML += `<div class="d-flex justify-content-between p-2 border-bottom"><div><span class="fw-bold">${d.dishName}</span> <br><small class="text-muted">${d.quantity} x ${(d.totalAmount / d.quantity).toLocaleString()}</small>${d.note ? `<br><small class="text-warning fst-italic">"${d.note}"</small>` : ''}</div><div class="text-end"><div class="fw-bold">${d.totalAmount.toLocaleString()}</div><span class="badge ${badge}">${txt}</span>${cancelBtn}</div></div>`;
    });
}
// --- MENU & SELECTION ---
async function loadMenuData() { const res = await fetch(`${API_URL}/Menu`); appState.categories = await res.json(); }
function openMenuSelection() { appState.tempMenuSelection = {}; appState.currentMenuCategory = 'All'; renderMenuUI(); showView('view-menu'); }

function renderMenuUI() {
    const catBar = document.getElementById('categoryBar'); const dishList = document.getElementById('dishList');
    catBar.innerHTML = ''; dishList.innerHTML = '';

    // 1. Tab Tất Cả
    const btnAll = document.createElement('button');
    btnAll.className = `filter-btn ${appState.currentMenuCategory === 'All' ? 'active' : ''}`;
    btnAll.innerText = "Tất cả";
    btnAll.onclick = () => { appState.currentMenuCategory = 'All'; renderMenuUI(); };
    catBar.appendChild(btnAll);

    appState.categories.forEach((cat) => {
        // [MODIFIED] Filter logic
        const btn = document.createElement('button');
        btn.className = `filter-btn ${appState.currentMenuCategory === cat.categoryID ? 'active' : ''}`;
        btn.innerText = cat.categoryName;
        btn.onclick = () => { appState.currentMenuCategory = cat.categoryID; renderMenuUI(); };
        catBar.appendChild(btn);

        // [MODIFIED] Chỉ render category đc chọn (Hoặc All)
        if (appState.currentMenuCategory !== 'All' && appState.currentMenuCategory !== cat.categoryID) return;

        const catHeader = document.createElement('h6'); catHeader.className = 'menu-category-header bg-light p-2 m-0 border-top border-bottom text-uppercase text-secondary fw-bold'; catHeader.innerText = cat.categoryName; catHeader.id = `cat-${cat.categoryID}`; dishList.appendChild(catHeader);

        (cat.dishes || cat.Dishes || []).forEach(dish => {
            const wrapper = document.createElement('div'); wrapper.dataset.id = dish.dishID;
            const selection = appState.tempMenuSelection[dish.dishID];
            const qty = selection ? selection.qty : 0;
            const note = selection ? selection.note : "";

            // [NEW] Tính số lượng đã có trong Cart (Status = New)
            const cartQty = appState.orderDetails
                .filter(d => d.dishID === dish.dishID && d.itemStatus === 'New')
                .reduce((sum, d) => sum + d.quantity, 0);

            // 2. UI Chọn món mới (Click hiện controls)
            if (qty === 0) {
                wrapper.className = 'dish-item';
                // Nếu có trong giỏ hàng thì highlight nhẹ hoặc hiện số lượng
                const cartBadge = cartQty > 0 ? `<span class="badge bg-danger rounded-pill ms-2">${cartQty}</span>` : '';

                wrapper.innerHTML = `<div class="w-100" onclick="incrementDish(${dish.dishID})"><div class="d-flex justify-content-between align-items-center"><h6 class="m-0">${dish.dishName} ${cartBadge}</h6><div class="fw-bold text-primary">${dish.price.toLocaleString()}đ</div></div></div>`;
            } else {
                wrapper.className = 'dish-item bg-light border border-primary';

                // Nếu đang chọn thêm, cũng hiện số lượng đã có trong giỏ để user biết
                const cartInfo = cartQty > 0 ? `<div class="text-danger small fw-bold mb-1">Đã có trong giỏ: ${cartQty}</div>` : '';

                wrapper.innerHTML = `<div class="w-100"><div class="d-flex justify-content-between align-items-center mb-2"><h6 class="m-0 text-primary fw-bold">${dish.dishName}</h6><div class="fw-bold">${dish.price.toLocaleString()}đ</div></div>${cartInfo}<div class="d-flex justify-content-between align-items-center"><div class="btn-group btn-group-sm"><button class="btn btn-secondary" onclick="updateTempQty(${dish.dishID}, -1)">-</button><span class="btn btn-light border fw-bold" style="min-width:35px">${qty}</span><button class="btn btn-secondary" onclick="updateTempQty(${dish.dishID}, 1)">+</button></div><button class="btn btn-sm ${note ? 'btn-warning' : 'btn-outline-secondary'}" onclick="openNoteModal(${dish.dishID}, 'menu', '${note}')"><i class="fas fa-comment-dots"></i> ${note ? 'Sửa Note' : 'Ghi chú'}</button></div>${note ? `<div class="text-warning small fst-italic mt-1 ms-1"><i class="fas fa-pen"></i> ${note}</div>` : ''}</div>`;
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

    // [NEW] Ẩn/Hiện tiêu đề Category khi search
    const catHeaders = document.querySelectorAll('.menu-category-header');
    catHeaders.forEach(h => h.style.display = term ? 'none' : 'block');

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
    let payload = appState.orderDetails.length === 0 ? { tableID: appState.currentTableId, accID: currentUser.accID || 1, items: itemsToAdd } : { accID: currentUser.accID || 1, details: itemsToAdd };

    try {
        const res = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
        if (res.ok) {
            showToast("Đã thêm vào giỏ");
            appState.tempMenuSelection = {};
            showView('view-detail');
            document.querySelector('a[href="#tab-cart"]').click();
            loadOrderData(appState.currentTableId);

            // [MODIFIED] Không set Occupied tại đây nữa (User yêu cầu chỉ set khi gửi bếp)
            // await loadTables(false);
            // const currentTable = appState.tables.find(t => t.tableID === appState.currentTableId);
            // if (currentTable && currentTable.tableStatus === 'Occupied') {
            //     const badge = document.getElementById('detailTableStatus');
            //     badge.innerText = 'Đã gọi món';
            //     badge.className = 'badge bg-danger';
            // }

        } else { showToast("Lỗi: " + await res.text(), 'danger'); }
    } catch (e) { showToast("Lỗi kết nối", 'danger'); }
}

async function sendOrderToKitchen() {
    const btn = document.querySelector('#cartActionBar button'); btn.disabled = true;
    try { const res = await fetch(`${API_URL}/Order/${appState.currentTableId}/send?accID=${currentUser.accID || 0}`, { method: 'POST' }); if (res.ok) { showToast('Đã gửi bếp thành công!'); document.querySelector('a[href="#tab-confirmed"]').click(); loadOrderData(appState.currentTableId); } else { showToast('Không có món mới để gửi', 'warning'); } } catch (e) { showToast('Lỗi kết nối!', 'danger'); } finally { btn.disabled = false; }
}

function cancelMenuSelection() { appState.tempMenuSelection = {}; showView('view-detail'); }
function logout() { localStorage.removeItem('posUser'); window.location.href = 'index.html'; }

// --- 1. Gửi yêu cầu In ---
async function requestBillMobile() {
    if (!confirm("Gửi yêu cầu in bill cho thu ngân?")) return;
    try {
        await fetch(`${API_URL}/Order/${appState.currentTableId}/request-payment?accID=${(currentUser && currentUser.accID) ? currentUser.accID : 0}`, { method: 'POST' });
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
// --- 3. Thanh toán ---
async function doPaymentMobile(method = 'Cash') {
    if (!currentUser.canPayment) { showToast("Bạn không có quyền thanh toán!", "warning"); return; }

    // Đóng modal nếu đang mở
    closeModal('paymentModal');

    // Kiểm tra xem có OrderID hợp lệ không
    if (!appState.currentOrderId || appState.currentOrderId === 0) {
        showToast("Bàn này đang trống hoặc chưa có đơn hàng!", "warning");
        return;
    }

    // Thay confirm() của trình duyệt bằng popup confirmModal
    openConfirmModal('payment', { method });
}

// [NEW] Open Modal
function openPaymentChoiceModal() {
    toggleActionMenu(); // Đóng menu action sheet
    document.getElementById('paymentModal').style.display = 'flex';
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
let pendingPaymentMethod = 'Cash';

// --- 1. XỬ LÝ MENU TRƯỢT ---
function toggleActionMenu() {
    const sheet = document.getElementById('actionSheet');
    const overlay = document.getElementById('actionSheetOverlay');

    if (sheet.classList.contains('show')) {
        closeActionMenu(true);
    } else {
        // Cập nhật quyền trước khi hiện menu
        updateMenuPermissions();
        sheet.classList.add('show');
        overlay.style.display = 'block';

        history.pushState({ popup: 'actionSheet', view: getActiveViewId() }, '', '#action');
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

    // 4. In Tạm Tính (Cần quyền + Có đơn)
    const btnProv = document.getElementById('btnMenuProvisional');
    if (btnProv) btnProv.style.display = canPay ? 'flex' : 'none';
}

// --- 2. XỬ LÝ POPUP XÁC NHẬN (IN & THANH TOÁN) ---
function openConfirmModal(type, payload = null) {
    // Đóng action sheet nếu nó đang mở (tránh trường hợp gọi từ nơi khác mà bị bật ngược lại)
    const sheet = document.getElementById('actionSheet');
    if (sheet && sheet.classList.contains('show')) {
        toggleActionMenu();
    }
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
        pendingPaymentMethod = (payload && payload.method) ? payload.method : pendingPaymentMethod;
        const methodText = pendingPaymentMethod === 'Cash' ? 'Tiền mặt' : 'Chuyển khoản / QR';
        title.innerText = "Thanh toán";
        msg.innerText = `Xác nhận thanh toán (${methodText}) và in hóa đơn?`;
        btn.className = "btn btn-success";
        icon.className = "fas fa-money-bill-wave fa-3x text-success";
        btn.onclick = () => executePayment(pendingPaymentMethod);
    }
    else if (type === 'move') {
        title.innerText = "Xác nhận Chuyển bàn";
        const statusText = moveTarget.tableStatus !== 'Empty' ? "(Đang có khách)" : "";
        msg.innerHTML = `Chuyển đơn sang bàn <b>${moveTarget.tableName}</b> ${statusText}?`;
        btn.className = "btn btn-primary";
        icon.className = "fas fa-exchange-alt fa-3x text-primary";
        btn.onclick = executeMoveTableAction;
    }
    else if (type === 'provisional') { // [NEW]
        title.innerText = "In Tạm Tính";
        msg.innerText = "In phiếu tạm tính cho bàn này? (Bàn sẽ được đánh dấu đã in)";
        btn.className = "btn btn-info text-dark";
        icon.className = "fas fa-file-invoice-dollar fa-3x text-info";
        btn.onclick = executePrintProvisional;
    }
    else if (type === 'deleteCart') {
        title.innerText = "Xóa món";
        const dishName = payload && payload.dishName ? payload.dishName : "món này";
        const qtyText = payload && payload.qty ? ` (SL: ${payload.qty})` : "";
        msg.innerHTML = `Bạn có chắc muốn xóa <b>${dishName}</b>${qtyText} khỏi giỏ?`;
        btn.className = "btn btn-danger";
        icon.className = "fas fa-trash fa-3x text-danger";
        btn.onclick = executeDeleteCartItem;
    }

    document.getElementById('confirmModal').style.display = 'flex';
}

// Logic thực thi (API call)
async function executeRequestBill() {
    closeModal('confirmModal');
    try {
        await fetch(`${API_URL}/Order/${appState.currentTableId}/request-payment?accID=${(currentUser && currentUser.accID) ? currentUser.accID : 0}`, { method: 'POST' });
        showToast("Đã gửi yêu cầu!");
    } catch (e) { showToast("Lỗi kết nối", "danger"); }
}

async function executePayment(method = 'Cash') {
    closeModal('confirmModal');
    // Kiểm tra OrderID
    if (!appState.currentOrderId || appState.currentOrderId === 0) {
        showToast("Bàn này đang trống hoặc chưa có đơn hàng!", "warning");
        return;
    }

    const payload = {
        accID: currentUser.accID,
        orderID: appState.currentOrderId,
        paymentMethod: method,
        discountPercent: 0,
        discountAmount: 0
    };
    try {
        const res = await fetch(`${API_URL}/Order/checkout-mobile`, {
            method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload)
        });
        if (res.ok) {
            showToast("Thanh toán thành công!");
            // Reset lại trạng thái
            appState.currentOrderId = 0;
            appState.orderDetails = [];
            renderConfirmedTab();
            updateTabBadges();
            showView('view-tables');
        } else {
            showToast(await res.text(), "danger");
        }
    } catch (e) { showToast("Lỗi kết nối", "danger"); }
}

async function executePrintProvisional() {
    closeModal('confirmModal');
    try {
        const res = await fetch(`${API_URL}/Order/${appState.currentTableId}/print-provisional`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ accID: currentUser.accID })
        });
        if (res.ok) {
            showToast("Đã in tạm tính!");
            // loadTables(false) sẽ tự chạy do SignalR
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

// --- 4. XỬ LÝ HỦY MÓN (SMART CANCEL) ---
// Thay thế hàm openCancelModal cũ
function openCancelModalGroup(groupKey, maxQty, dishName) {
    if (!currentUser.canCancelItem) { showToast("Không có quyền hủy!", "warning"); return; }

    // Parse ngược lại key để tìm items
    // Key format: dishID|note|status|discountRate
    const parts = groupKey.split('|');
    const dishID = parseInt(parts[0]);
    const note = parts[1];
    const status = parts[2];
    const rate = parts.length > 3 ? parseFloat(parts[3]) : 0;

    // Tìm tất cả items match với group này
    const matchingItems = appState.orderDetails.filter(d =>
        d.dishID === dishID &&
        (d.note || "").trim() === note &&
        d.itemStatus === status &&
        Math.abs((d.discountRate || 0) - rate) < 0.0001
    );

    // Lưu danh sách items cần hủy vào state
    cancelState = {
        isGroup: true,
        items: matchingItems, // List of objects
        currentQty: 1,
        maxQty: maxQty
    };

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
    let qtyToCancel = cancelState.currentQty;
    let requests = [];

    // [LOGIC MỚI] Xây dựng danh sách hủy để gửi bulk
    const sortedItems = cancelState.items.sort((a, b) => b.orderDetailID - a.orderDetailID);

    for (const item of sortedItems) {
        if (qtyToCancel <= 0) break;

        const canCancel = Math.min(item.quantity, qtyToCancel);

        if (canCancel > 0) {
            requests.push({
                accID: currentUser.accID,
                orderDetailID: item.orderDetailID,
                quantity: canCancel,
                // reason: "Mobile Cancel"
            });
            qtyToCancel -= canCancel;
        }
    }

    if (requests.length > 0) {
        try {
            // Gọi API Bulk Cancel
            const res = await fetch(`${API_URL}/Order/cancel-multiple`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(requests)
            });

            if (res.ok) {
                showToast(`Đã hủy ${requests.length} yêu cầu thành công`);
                // Refresh
                await loadOrderData(appState.currentTableId);
                await loadTables(false);

                // Update badge UI
                const currentTable = appState.tables.find(t => t.tableID === appState.currentTableId);
                if (currentTable) {
                    const statusText = currentTable.tableStatus === 'Occupied' ? 'Đã gọi món' : 'Bàn trống';
                    const statusClass = currentTable.tableStatus === 'Occupied' ? 'bg-danger' : 'bg-success';
                    const badge = document.getElementById('detailTableStatus');
                    badge.innerText = statusText;
                    badge.className = `badge ${statusClass}`;
                }
            } else {
                showToast(await res.text(), "warning");
            }
        } catch (e) { showToast("Lỗi kết nối server", "danger"); }
    }
}


// --- UTILS ---
function closeModal(id) {
    document.getElementById(id).style.display = 'none';
}