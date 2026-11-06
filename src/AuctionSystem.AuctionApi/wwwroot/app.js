// Auction System Frontend Logic with SignalR & REST API

let currentAuction = null;
let currentBids = [];
let activeFilter = 'ALL';
let connection = null;
let countdownInterval = null;

// Format Currency VND
function formatVND(amount) {
    if (amount === null || amount === undefined) return '0 ₫';
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(amount);
}

// Format Time
function formatTime(isoString) {
    if (!isoString) return '';
    const date = new Date(isoString);
    return date.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit', second: '2-digit' }) + ' ' + 
           date.toLocaleDateString('vi-VN', { day: '2-digit', month: '2-digit' });
}

// Synthetic Gavel Chime using Web Audio API
function playChimeSound() {
    try {
        const ctx = new (window.AudioContext || window.webkitAudioContext)();
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.type = 'triangle';
        osc.frequency.setValueAtTime(587.33, ctx.currentTime);
        osc.frequency.exponentialRampToValueAtTime(880, ctx.currentTime + 0.15);
        gain.gain.setValueAtTime(0.25, ctx.currentTime);
        gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.35);
        osc.connect(gain);
        gain.connect(ctx.destination);
        osc.start();
        osc.stop(ctx.currentTime + 0.35);
    } catch (_) {}
}

// Reset Demo Data
async function resetDemoData() {
    if (!confirm("Khôi phục toàn bộ các phiên đấu giá mẫu ban đầu?")) return;
    try {
        const res = await fetch('/auctions/reset-demo', { method: 'POST' });
        const data = await res.json();
        showToast("Đã khôi phục!", data.message, "success");
        currentAuction = null;
        await loadAuctions();
    } catch (err) {
        showToast("Lỗi", err.message, "error");
    }
}

// Initialize SignalR Connection
function setupSignalR() {
    connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/auction")
        .withAutomaticReconnect()
        .build();

    // Event: Receive Bid from RabbitMQ -> SignalR
    connection.on("ReceiveBid", (event) => {
        console.log("[SignalR ReceiveBid]", event);

        // Update list card
        updateCardPrice(event.auctionId, event.newCurrentPrice, event.bidderName);

        if (currentAuction && currentAuction.id === event.auctionId) {
            currentAuction.currentPrice = event.newCurrentPrice;
            currentAuction.highestBidder = event.bidderName;
            currentAuction.totalBids = (currentAuction.totalBids || 0) + 1;

            renderActiveAuctionHeader();

            // Prepend new bid to bid log
            const newBid = {
                id: event.bidId,
                auctionId: event.auctionId,
                bidderName: event.bidderName,
                amount: event.amount,
                timestamp: event.timestamp,
                isWinning: false
            };
            currentBids.unshift(newBid);
            renderBidsTable(true);

            showToast("Giá mới được đặt!", `${event.bidderName} vừa trả ${formatVND(event.amount)}`, "info");
        }
    });

    // Event: Auction Ended
    connection.on("AuctionEnded", (event) => {
        console.log("[SignalR AuctionEnded]", event);
        if (currentAuction && currentAuction.id === event.auctionId) {
            currentAuction.status = "Ended";
            currentAuction.winnerName = event.winnerName;
            currentAuction.winningBid = event.winningBid;
            renderActiveAuctionHeader();
            showToast("Phiên kết thúc!", event.hasWinner ? `Người thắng: ${event.winnerName} (${formatVND(event.winningBid)})` : "Không có ai đặt giá.", "warning");
        }
        loadAuctions();
    });

    // Connection lifecycle
    connection.onreconnecting(() => {
        document.getElementById('signalrStatusPill').className = "px-2.5 py-1 rounded-full bg-amber-950 text-amber-400 border border-amber-800 flex items-center gap-1.5";
        document.getElementById('signalrDot').className = "w-2 h-2 rounded-full bg-amber-500 animate-pulse";
    });

    connection.onreconnected(() => {
        document.getElementById('signalrStatusPill').className = "px-2.5 py-1 rounded-full bg-purple-950 text-purple-400 border border-purple-800 flex items-center gap-1.5";
        document.getElementById('signalrDot').className = "w-2 h-2 rounded-full bg-purple-500";
        if (currentAuction) {
            connection.invoke("JoinAuction", currentAuction.id);
        }
    });

    connection.start()
        .then(() => {
            console.log("[SignalR] Connected successfully.");
            document.getElementById('signalrStatusPill').className = "px-2.5 py-1 rounded-full bg-purple-950 text-purple-400 border border-purple-800 flex items-center gap-1.5";
        })
        .catch(err => {
            console.error("[SignalR Connection Error]", err);
            document.getElementById('signalrStatusPill').className = "px-2.5 py-1 rounded-full bg-rose-950 text-rose-400 border border-rose-800 flex items-center gap-1.5";
        });
}

// Load Auctions List (REST API GET /auctions)
async function loadAuctions() {
    try {
        const res = await fetch('/auctions');
        const auctions = await res.json();

        document.getElementById('auctionCountBadge').innerText = auctions.length;

        const container = document.getElementById('auctionListContainer');
        container.innerHTML = '';

        const filtered = auctions.filter(a => {
            if (activeFilter === 'ALL') return true;
            return a.status.toLowerCase() === activeFilter.toLowerCase();
        });

        if (filtered.length === 0) {
            container.innerHTML = `<div class="text-center py-10 text-slate-500 text-sm">Không có phiên đấu giá nào phù hợp.</div>`;
            return;
        }

        filtered.forEach(a => {
            const isSelected = currentAuction && currentAuction.id === a.id;
            const statusBadge = a.status === 'Active'
                ? `<span class="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-emerald-500/20 text-emerald-400 border border-emerald-500/40">Đang mở</span>`
                : (a.status === 'Ended'
                    ? `<span class="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-rose-500/20 text-rose-400 border border-rose-500/40">Đã kết thúc</span>`
                    : `<span class="px-2 py-0.5 rounded-full text-[10px] font-semibold bg-slate-600 text-slate-300">Sắp diễn ra</span>`);

            const card = document.createElement('div');
            card.id = `card-${a.id}`;
            card.className = `p-4 rounded-xl border cursor-pointer transition flex flex-col space-y-2 ${isSelected ? 'bg-slate-700/80 border-indigo-500 shadow-md shadow-indigo-500/10 ring-1 ring-indigo-500' : 'bg-slate-800/60 border-slate-700 hover:bg-slate-700/40 hover:border-slate-600'}`;
            card.onclick = () => selectAuction(a.id);

            card.innerHTML = `
                <div class="flex justify-between items-start gap-2">
                    <h3 class="font-bold text-white text-sm line-clamp-1">${a.title}</h3>
                    ${statusBadge}
                </div>
                <div class="flex justify-between items-baseline pt-1">
                    <span class="text-xs text-slate-400">Giá hiện tại:</span>
                    <span id="price-${a.id}" class="text-base font-extrabold text-amber-400">${formatVND(a.currentPrice)}</span>
                </div>
                <div class="flex justify-between items-center text-xs text-slate-400 border-t border-slate-700/50 pt-2">
                    <span><i class="fa-solid fa-crown text-amber-500/70 mr-1"></i><span id="bidder-${a.id}">${a.highestBidder || 'Chưa có'}</span></span>
                    <span><i class="fa-solid fa-clock mr-1 text-slate-500"></i>${Math.round(a.remainingSeconds)}s</span>
                </div>
            `;
            container.appendChild(card);
        });

        // If no auction selected yet, select the first active auction
        if (!currentAuction && filtered.length > 0) {
            const firstActive = filtered.find(a => a.status === 'Active') || filtered[0];
            selectAuction(firstActive.id);
        }
    } catch (err) {
        console.error("Error loading auctions", err);
    }
}

// Select an Auction -> Joins SignalR group & loads detail
async function selectAuction(id) {
    if (currentAuction && connection && connection.state === signalR.HubConnectionState.Connected) {
        connection.invoke("LeaveAuction", currentAuction.id).catch(console.error);
    }

    clearInterval(countdownInterval);

    try {
        const res = await fetch(`/auctions/${id}`);
        if (!res.ok) throw new Error("Auction not found");
        currentAuction = await res.json();

        // Join SignalR group
        if (connection && connection.state === signalR.HubConnectionState.Connected) {
            connection.invoke("JoinAuction", currentAuction.id).catch(console.error);
        }

        renderActiveAuctionHeader();
        loadBids(id);
        loadAudits(id);
        startCountdown();

        // Highlight selected card
        document.querySelectorAll('#auctionListContainer > div').forEach(c => {
            c.classList.remove('bg-slate-700/80', 'border-indigo-500', 'ring-1', 'ring-indigo-500');
            c.classList.add('bg-slate-800/60', 'border-slate-700');
        });
        const currentCard = document.getElementById(`card-${id}`);
        if (currentCard) {
            currentCard.classList.remove('bg-slate-800/60', 'border-slate-700');
            currentCard.classList.add('bg-slate-700/80', 'border-indigo-500', 'ring-1', 'ring-indigo-500');
        }
    } catch (err) {
        console.error("Error selecting auction", err);
    }
}

// Render Header & Bidding Control for Active Auction
function renderActiveAuctionHeader() {
    if (!currentAuction) return;

    const card = document.getElementById('auctionDetailCard');
    const isEnded = currentAuction.status === 'Ended';
    const suggestedMin = (currentAuction.currentPrice > 0 ? currentAuction.currentPrice : currentAuction.startingPrice) + currentAuction.minStep;

    let statusHeader = '';
    if (isEnded) {
        statusHeader = `
            <div class="bg-gradient-to-r from-rose-900/60 via-purple-900/40 to-slate-800 border border-rose-700/60 rounded-xl p-4 mb-4 flex items-center justify-between">
                <div class="flex items-center space-x-3">
                    <div class="w-10 h-10 rounded-full bg-rose-500/20 text-rose-400 flex items-center justify-center text-xl">
                        <i class="fa-solid fa-trophy"></i>
                    </div>
                    <div>
                        <h4 class="font-bold text-white text-base">Phiên Đấu Giá Đã Kết Thúc</h4>
                        <p class="text-xs text-rose-300">
                            ${currentAuction.winnerName ? `Người chiến thắng: <b class="text-white">${currentAuction.winnerName}</b> với mức giá <b class="text-amber-400">${formatVND(currentAuction.winningBid)}</b>` : 'Không có ai tham gia trả giá cho phiên này.'}
                        </p>
                    </div>
                </div>
                <span class="px-3 py-1 rounded-full bg-rose-950 text-rose-400 border border-rose-800 text-xs font-semibold">ĐÃ KẾT THÚC</span>
            </div>
        `;
    }

    card.innerHTML = `
        ${statusHeader}
        <div class="flex flex-col md:flex-row justify-between items-start md:items-center gap-4 border-b border-slate-700/60 pb-5">
            <div>
                <div class="flex items-center gap-2 mb-1">
                    <span class="px-2.5 py-0.5 rounded-full text-xs font-semibold ${isEnded ? 'bg-rose-900 text-rose-300' : 'bg-emerald-900 text-emerald-300'}">
                        ${isEnded ? 'Đã kết thúc' : 'Đang diễn ra'}
                    </span>
                    <span class="text-xs text-slate-400">ID: ${currentAuction.id}</span>
                    <span class="text-xs text-slate-400">&bull; Người bán: <b class="text-slate-300">${currentAuction.sellerName}</b></span>
                </div>
                <h2 class="text-2xl font-black text-white">${currentAuction.title}</h2>
                <p class="text-xs text-slate-400 mt-1 max-w-xl">${currentAuction.description || 'Không có mô tả bổ sung.'}</p>
            </div>

            <!-- Countdown Timer -->
            <div class="bg-slate-950 px-5 py-3 rounded-2xl border border-slate-700 flex items-center space-x-3 shadow-inner">
                <i class="fa-solid fa-stopwatch text-amber-400 text-2xl"></i>
                <div>
                    <span class="text-[10px] uppercase font-bold text-slate-400 block tracking-wider">Thời gian còn lại</span>
                    <span id="countdownDisplay" class="text-xl font-mono font-bold text-white">--:--</span>
                </div>
            </div>
        </div>

        <!-- Price Display Grid -->
        <div class="grid grid-cols-1 md:grid-cols-3 gap-4 my-6">
            <!-- Starting Price -->
            <div class="bg-slate-900/80 p-4 rounded-xl border border-slate-700/60">
                <span class="text-xs text-slate-400 font-medium block">Giá khởi điểm</span>
                <span class="text-lg font-bold text-slate-200 mt-0.5 block">${formatVND(currentAuction.startingPrice)}</span>
                <span class="text-[11px] text-slate-500 mt-1 block">Bước giá: +${formatVND(currentAuction.minStep)}</span>
            </div>

            <!-- Current Price (Glows on update) -->
            <div class="bg-slate-900/90 p-4 rounded-xl border border-amber-500/40 relative overflow-hidden glow-price">
                <span class="text-xs font-bold text-amber-400 uppercase tracking-wider block">Giá hiện tại</span>
                <span id="activeCurrentPrice" class="text-2xl font-black text-amber-400 mt-0.5 block transition-all">${formatVND(currentAuction.currentPrice)}</span>
                <span class="text-[11px] text-slate-400 mt-1 block flex items-center gap-1">
                    <i class="fa-solid fa-crown text-amber-400"></i> Người giữ giá: <b class="text-white">${currentAuction.highestBidder || 'Chưa có'}</b>
                </span>
            </div>

            <!-- Fast Quick Bids -->
            <div class="bg-slate-900/80 p-4 rounded-xl border border-slate-700/60 flex flex-col justify-center">
                <span class="text-xs text-slate-400 font-medium mb-1.5 block">Chọn nhanh mức giá gợi ý</span>
                <div class="flex gap-2">
                    <button onclick="setBidAmount(${suggestedMin})" ${isEnded ? 'disabled' : ''} class="flex-1 py-1.5 rounded-lg bg-indigo-900/60 hover:bg-indigo-700 text-indigo-300 text-xs font-semibold transition border border-indigo-700 disabled:opacity-40">
                        +Bước giá
                    </button>
                    <button onclick="setBidAmount(${suggestedMin + currentAuction.minStep})" ${isEnded ? 'disabled' : ''} class="flex-1 py-1.5 rounded-lg bg-indigo-900/60 hover:bg-indigo-700 text-indigo-300 text-xs font-semibold transition border border-indigo-700 disabled:opacity-40">
                        +2x Bước
                    </button>
                    ${!isEnded ? `
                    <button onclick="endAuctionNow()" title="Kết thúc ngay để kiểm tra kết quả & Audit Worker" class="px-2.5 py-1.5 rounded-lg bg-rose-900/50 hover:bg-rose-800 text-rose-300 text-xs font-semibold transition border border-rose-800">
                        <i class="fa-solid fa-flag-checkered"></i> Chốt
                    </button>
                    ` : ''}
                </div>
            </div>
        </div>

        <!-- Bidding Input Box -->
        <div class="bg-slate-950/80 p-4 rounded-xl border border-slate-700 flex flex-col md:flex-row items-center justify-between gap-4">
            <div class="w-full md:w-auto flex-1 flex items-center gap-3">
                <div class="relative flex-1">
                    <span class="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none text-slate-500 text-sm">
                        <i class="fa-solid fa-coins"></i>
                    </span>
                    <input id="bidAmountInput" type="number" step="${currentAuction.minStep}" min="${suggestedMin}" value="${suggestedMin}"
                           ${isEnded ? 'disabled' : ''}
                           placeholder="Nhập mức giá bạn muốn trả..." 
                           class="w-full bg-slate-900 border border-slate-700 rounded-lg pl-9 pr-3 py-2.5 text-base font-bold text-amber-400 focus:outline-none focus:border-amber-400 disabled:opacity-40" />
                </div>
            </div>

            <button onclick="submitBid()" ${isEnded ? 'disabled' : ''} 
                    class="w-full md:w-auto px-6 py-2.5 rounded-lg bg-gradient-to-r from-amber-500 to-amber-600 hover:from-amber-400 hover:to-amber-500 text-slate-950 font-black text-sm uppercase tracking-wide transition shadow-lg shadow-amber-500/20 disabled:opacity-40 disabled:cursor-not-allowed flex items-center justify-center gap-2">
                <i class="fa-solid fa-gavel"></i> Gửi Mức Giá (REST -> gRPC)
            </button>
        </div>

        <div id="validationAlert" class="mt-3 hidden text-xs p-3 rounded-lg bg-rose-950/80 border border-rose-800 text-rose-300 flex items-center gap-2">
            <i class="fa-solid fa-circle-exclamation text-rose-400 text-sm"></i>
            <span id="validationAlertMsg"></span>
        </div>
    `;
}

function setBidAmount(amount) {
    const input = document.getElementById('bidAmountInput');
    if (input) {
        input.value = amount;
    }
}

// Submit Bid (REST API POST /auctions/{id}/bids)
async function submitBid() {
    if (!currentAuction) return;

    const bidderName = document.getElementById('currentBidderInput').value.trim();
    const amount = parseFloat(document.getElementById('bidAmountInput').value);

    if (!bidderName) {
        showToast("Lỗi", "Vui lòng nhập tên người trả giá của bạn ở góc trên.", "error");
        return;
    }

    if (isNaN(amount) || amount <= 0) {
        showToast("Lỗi", "Mức giá không hợp lệ.", "error");
        return;
    }

    const alertBox = document.getElementById('validationAlert');
    const alertMsg = document.getElementById('validationAlertMsg');
    alertBox.classList.add('hidden');

    try {
        const res = await fetch(`/auctions/${currentAuction.id}/bids`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ bidderName, amount })
        });

        const data = await res.json();

        if (!res.ok || !data.success) {
            // gRPC Validation Error returned!
            console.warn("[gRPC Validation Error]", data);
            alertMsg.innerText = `[gRPC Validator từ chối] ${data.message || 'Mức giá không hợp lệ'}`;
            alertBox.classList.remove('hidden');

            if (data.suggestedMinBid) {
                setBidAmount(data.suggestedMinBid);
            }
            showToast("gRPC Kiểm Tra Thất Bại", data.message, "error");
        } else {
            // Success
            showToast("Thành công!", `Mức giá ${formatVND(amount)} đã được ghi nhận và phát tới RabbitMQ!`, "success");
            alertBox.classList.add('hidden');
        }
    } catch (err) {
        console.error("Error submitting bid", err);
        showToast("Lỗi kết nối", err.message, "error");
    }
}

// Force-end auction for instant demo
async function endAuctionNow() {
    if (!currentAuction) return;
    if (!confirm(`Bạn có chắc muốn kết thúc ngay phiên '${currentAuction.title}' để kiểm tra xác định người thắng và ghi Audit Worker không?`)) return;

    try {
        const res = await fetch(`/auctions/${currentAuction.id}/end`, { method: 'POST' });
        const updated = await res.json();
        showToast("Đã chốt phiên!", `Phiên đấu giá đã kết thúc. Kiểm tra kết quả trong tab Nhật ký!`, "info");
        selectAuction(currentAuction.id);
    } catch (err) {
        console.error("Error ending auction", err);
    }
}

// Load Bid History (REST API GET /auctions/{id}/bids)
async function loadBids(auctionId) {
    try {
        const res = await fetch(`/auctions/${auctionId}/bids`);
        currentBids = await res.json();
        renderBidsTable(false);
    } catch (err) {
        console.error("Error loading bids", err);
    }
}

function renderBidsTable(isHighlightTop = false) {
    const tbody = document.getElementById('bidLogBody');
    document.getElementById('logCounter').innerText = `${currentBids.length} lượt`;

    if (currentBids.length === 0) {
        tbody.innerHTML = `<tr><td colspan="4" class="text-center py-8 text-slate-500">Chưa có lượt trả giá nào cho phiên này.</td></tr>`;
        return;
    }

    tbody.innerHTML = '';
    currentBids.forEach((bid, index) => {
        const tr = document.createElement('tr');
        if (index === 0 && isHighlightTop) {
            tr.className = 'row-highlight';
        }

        const isWinningBadge = index === 0
            ? `<span class="px-2 py-0.5 rounded text-[10px] font-bold bg-amber-500/20 text-amber-400 border border-amber-500/30"><i class="fa-solid fa-crown mr-1"></i>Cao nhất</span>`
            : `<span class="px-2 py-0.5 rounded text-[10px] text-slate-400 bg-slate-800">Đã vượt qua</span>`;

        tr.innerHTML = `
            <td class="py-2.5 text-xs text-slate-400 font-mono">${formatTime(bid.timestamp)}</td>
            <td class="py-2.5 font-semibold text-white flex items-center gap-2">
                <div class="w-6 h-6 rounded-full bg-slate-700 flex items-center justify-center text-[10px] text-indigo-400">
                    <i class="fa-solid fa-user"></i>
                </div>
                ${bid.bidderName}
            </td>
            <td class="py-2.5 text-right font-extrabold text-amber-400">${formatVND(bid.amount)}</td>
            <td class="py-2.5 text-center">${isWinningBadge}</td>
        `;
        tbody.appendChild(tr);
    });
}

// Load Audit Logs (REST API GET /auctions/{id}/audits)
async function loadAudits(auctionId) {
    try {
        const res = await fetch(`/auctions/${auctionId}/audits`);
        const logs = await res.json();
        const list = document.getElementById('auditLogList');

        if (logs.length === 0) {
            list.innerHTML = `<div class="text-center py-8 text-slate-500 text-xs">Chưa có bản ghi Audit Worker nào cho phiên này (Audit Worker ghi khi có sự kiện từ RabbitMQ).</div>`;
            return;
        }

        list.innerHTML = '';
        logs.forEach(l => {
            const item = document.createElement('div');
            item.className = 'p-3 rounded-lg bg-slate-900/60 border border-slate-700/60 text-xs flex justify-between items-center';
            item.innerHTML = `
                <div>
                    <span class="font-bold ${l.eventType === 'BidAccepted' ? 'text-emerald-400' : 'text-amber-400'}">[${l.eventType}]</span>
                    <span class="text-slate-300 ml-2">${l.details}</span>
                </div>
                <span class="text-[10px] text-slate-500 font-mono">${formatTime(l.timestamp)}</span>
            `;
            list.appendChild(item);
        });
    } catch (err) {
        console.error("Error loading audits", err);
    }
}

// Switch between Bids Log & Audit Log tabs
function switchLogTab(tab) {
    const bidsContainer = document.getElementById('bidsTableContainer');
    const auditContainer = document.getElementById('auditTableContainer');
    const tabBid = document.getElementById('tabBidLog');
    const tabAudit = document.getElementById('tabAuditLog');

    if (tab === 'bids') {
        bidsContainer.classList.remove('hidden');
        auditContainer.classList.add('hidden');
        tabBid.className = "text-sm font-bold text-amber-400 border-b-2 border-amber-400 pb-1 flex items-center gap-2";
        tabAudit.className = "text-sm font-medium text-slate-400 hover:text-white border-b-2 border-transparent pb-1 flex items-center gap-2";
    } else {
        bidsContainer.classList.add('hidden');
        auditContainer.classList.remove('hidden');
        tabBid.className = "text-sm font-medium text-slate-400 hover:text-white border-b-2 border-transparent pb-1 flex items-center gap-2";
        tabAudit.className = "text-sm font-bold text-emerald-400 border-b-2 border-emerald-400 pb-1 flex items-center gap-2";
        if (currentAuction) {
            loadAudits(currentAuction.id);
        }
    }
}

// Filter auctions
function setFilter(filter) {
    activeFilter = filter;
    ['tabALL', 'tabActive', 'tabEnded'].forEach(id => {
        const btn = document.getElementById(id);
        btn.className = "flex-1 py-1.5 rounded-md text-center text-slate-400 hover:text-white transition";
    });
    document.getElementById(`tab${filter}`).className = "flex-1 py-1.5 rounded-md text-center bg-indigo-600 text-white transition";
    loadAuctions();
}

function updateCardPrice(auctionId, newPrice, bidder) {
    const priceEl = document.getElementById(`price-${auctionId}`);
    const bidderEl = document.getElementById(`bidder-${auctionId}`);
    if (priceEl) priceEl.innerText = formatVND(newPrice);
    if (bidderEl) bidderEl.innerText = bidder;
}

// Countdown Timer logic
function startCountdown() {
    clearInterval(countdownInterval);
    const display = document.getElementById('countdownDisplay');

    countdownInterval = setInterval(() => {
        if (!currentAuction || currentAuction.status === 'Ended') {
            if (display) display.innerText = "00:00:00";
            return;
        }

        const now = new Date().getTime();
        const end = new Date(currentAuction.endTime).getTime();
        const diff = Math.max(0, Math.floor((end - now) / 1000));

        if (diff <= 0) {
            clearInterval(countdownInterval);
            if (display) display.innerText = "00:00:00";
            currentAuction.status = "Ended";
            renderActiveAuctionHeader();
            return;
        }

        const hours = Math.floor(diff / 3600);
        const minutes = Math.floor((diff % 3600) / 60);
        const seconds = diff % 60;

        if (display) {
            display.innerText = `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
        }
    }, 1000);
}

// Create Auction Modal Handler
function openCreateModal() {
    document.getElementById('createModal').classList.remove('hidden');
}

function closeCreateModal() {
    document.getElementById('createModal').classList.add('hidden');
}

async function handleCreateAuction(event) {
    event.preventDefault();

    const title = document.getElementById('inputTitle').value.trim();
    const description = document.getElementById('inputDescription').value.trim();
    const startingPrice = parseFloat(document.getElementById('inputStartingPrice').value);
    const minStep = parseFloat(document.getElementById('inputMinStep').value);
    const durationMinutes = parseInt(document.getElementById('inputDuration').value);
    const sellerName = document.getElementById('inputSeller').value.trim();

    try {
        const res = await fetch('/auctions', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                title,
                description,
                startingPrice,
                minStep,
                durationMinutes,
                sellerName
            })
        });

        if (!res.ok) {
            const err = await res.json();
            throw new Error(err.message || 'Không thể tạo phiên');
        }

        const newAuction = await res.json();
        closeCreateModal();
        showToast("Thành công!", `Đã tạo phiên đấu giá '${newAuction.title}'`, "success");

        await loadAuctions();
        selectAuction(newAuction.id);
    } catch (err) {
        showToast("Lỗi", err.message, "error");
    }
}

// Toast Notifications
function showToast(title, msg, type = 'info') {
    const toast = document.getElementById('toast');
    const toastBox = document.getElementById('toastBox');
    const toastTitle = document.getElementById('toastTitle');
    const toastMsg = document.getElementById('toastMsg');
    const toastIcon = document.getElementById('toastIcon');

    toastTitle.innerText = title;
    toastMsg.innerText = msg;

    if (type === 'success') {
        toastIcon.className = "fa-solid fa-circle-check text-emerald-400 text-lg";
        toastBox.className = "bg-slate-800 border border-emerald-600 text-white px-4 py-3 rounded-xl shadow-2xl flex items-center space-x-3 max-w-md";
    } else if (type === 'error') {
        toastIcon.className = "fa-solid fa-circle-xmark text-rose-400 text-lg";
        toastBox.className = "bg-slate-800 border border-rose-600 text-white px-4 py-3 rounded-xl shadow-2xl flex items-center space-x-3 max-w-md";
    } else {
        toastIcon.className = "fa-solid fa-circle-info text-indigo-400 text-lg";
        toastBox.className = "bg-slate-800 border border-indigo-600 text-white px-4 py-3 rounded-xl shadow-2xl flex items-center space-x-3 max-w-md";
    }

    toast.classList.remove('translate-y-24', 'opacity-0');

    setTimeout(() => {
        toast.classList.add('translate-y-24', 'opacity-0');
    }, 4000);
}

// App Initialization
window.addEventListener('DOMContentLoaded', () => {
    setupSignalR();
    loadAuctions();
});

