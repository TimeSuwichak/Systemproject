(() => {
  const storageKey = "old-things-auction-v1";
  const imageFallback = "https://images.unsplash.com/photo-1577083552431-6e5fd01aa342?auto=format&fit=crop&w=800&q=80";
  const seedProducts = [
    { id: "knife", name: "มีดครัวญี่ปุ่นตีมือ", category: "เครื่องครัว", condition: "สภาพดี · ราวปี 1970", price: 1250, startPrice: 800, increment: 100, bids: 8, endsIn: 8240, image: "https://images.unsplash.com/photo-1593618998160-e34014e67546?auto=format&fit=crop&w=800&q=80", description: "มีดครัวญี่ปุ่นงานตีมือ ใบมีดเหล็กคาร์บอน ลับคมใหม่พร้อมใช้งาน ด้ามไม้เดิมมีร่องรอยตามอายุ เหมาะกับนักสะสมเครื่องครัววินเทจ", seller: "คุณชาญชัย", history: [{ name: "คุณปกรณ์", price: 1250, time: "11:24" }, { name: "คุณพิมพ์ชนก", price: 1150, time: "11:02" }, { name: "คุณณัฐ", price: 1050, time: "10:48" }] },
    { id: "tea", name: "ชุดชงชาเซรามิกญี่ปุ่น", category: "ชาและเซรามิก", condition: "สภาพดีมาก · ราวปี 1960", price: 2800, startPrice: 1800, increment: 200, bids: 12, endsIn: 19330, image: "https://images.unsplash.com/photo-1578749556568-bc2c40e68b61?auto=format&fit=crop&w=800&q=80", description: "ชุดชงชาประกอบด้วยกาน้ำชาและถ้วย 4 ใบ เคลือบสีงาช้างลายคราม งานทำมือจากญี่ปุ่น ไม่มีรอยร้าวหรือบิ่น", seller: "คุณรัตนา", history: [{ name: "คุณภัทร", price: 2800, time: "11:30" }, { name: "คุณอร", price: 2600, time: "11:12" }, { name: "คุณณัฐ", price: 2400, time: "10:56" }] },
    { id: "coin", name: "เหรียญ 1 บาท ร.5 พ.ศ. 2451", category: "เหรียญสะสม", condition: "ผ่านการใช้งาน · หายาก", price: 1950, startPrice: 1200, increment: 50, bids: 17, endsIn: 2930, image: "https://images.unsplash.com/photo-1605100804763-247f67b3557e?auto=format&fit=crop&w=800&q=80", description: "เหรียญเงินรัชกาลที่ 5 พ.ศ. 2451 สภาพผ่านการใช้งานตามอายุ รายละเอียดด้านหน้าและหลังยังชัดเจน มีภาพถ่ายขยายให้ตรวจสอบ", seller: "คุณธนกร", history: [{ name: "คุณพิมพ์ชนก", price: 1950, time: "11:40" }, { name: "คุณกิตติ", price: 1900, time: "11:31" }, { name: "คุณสุนิสา", price: 1850, time: "11:25" }] },
    { id: "vase", name: "แจกันเซรามิกลายคราม", category: "ชาและเซรามิก", condition: "สภาพดี · มีอายุราว 50 ปี", price: 1600, startPrice: 1000, increment: 100, bids: 6, endsIn: 31500, image: "https://images.unsplash.com/photo-1578500494198-246f612d3b3d?auto=format&fit=crop&w=800&q=80", description: "แจกันเซรามิกลายครามทรงคลาสสิก เคลือบเงา ลวดลายเขียนมือ ไม่มีรอยร้าว เหมาะสำหรับจัดแสดงหรือสะสม", seller: "คุณศิริพร", history: [{ name: "คุณอร", price: 1600, time: "11:18" }, { name: "คุณสมชาย", price: 1500, time: "11:05" }] }
  ];
  const seedPending = [
    { id: "pending-1", name: "กระทะเหล็กหล่อโบราณ", seller: "คุณวิชัย", category: "เครื่องครัว", price: 650, startAt: "12 พ.ย. 2569 · 09:00", endAt: "13 พ.ย. 2569 · 18:00", image: "https://images.unsplash.com/photo-1590794056226-79ef3a8147e1?auto=format&fit=crop&w=180&q=75" },
    { id: "pending-2", name: "ถ้วยชาลายครามคู่", seller: "คุณมาลี", category: "ชาและเซรามิก", price: 900, startAt: "12 พ.ย. 2569 · 10:00", endAt: "14 พ.ย. 2569 · 18:00", image: "https://images.unsplash.com/photo-1578749556568-bc2c40e68b61?auto=format&fit=crop&w=180&q=75" }
  ];
  const defaultState = {
    products: seedProducts,
    saved: [],
    bidHistory: [],
    pending: seedPending,
    sellerItems: [],
    winners: [{ id: "winner-coin", name: "เหรียญ 1 บาท ร.5", category: "เหรียญสะสม", price: 2450, winner: "คุณธนกร", date: "10 พ.ย. 2569", status: "นัดรับสำเร็จ", image: seedProducts[2].image }],
    payments: [{ id: "payment-1", name: "ถ้วยชาลายครามวินเทจ", category: "ชาและเซรามิก", price: 1800, discount: 15, image: seedProducts[1].image, seller: "คุณรัตนา", paid: false }]
  };
  const categoryDiscounts = { "เครื่องครัว": 10, "ชาและเซรามิก": 15, "เหรียญสะสม": 8, "ของแต่งบ้าน": 10 };
  const pageTitles = { today: "ประมูลวันนี้", mybids: "รายการที่สนใจ", sell: "ลงขายสินค้า", messages: "กล่องข้อความ", payments: "ชำระเงิน", admin: "ตรวจสอบสินค้า", winners: "ประวัติผู้ชนะ" };
  const toastRegion = document.getElementById("toastRegion");
  let state = defaultState;
  let activeCategory = "all";
  let sortMode = "ending";
  let selectedProductId = null;
  let searchTerm = "";

  function showToast(message, isError = false) {
    const toast = document.createElement("div");
    toast.className = `toast${isError ? " error" : ""}`;
    toast.textContent = message;
    toastRegion.append(toast);
    window.setTimeout(() => toast.remove(), 3400);
  }

  try {
    const savedState = window.localStorage.getItem(storageKey);
    if (savedState) {
      const parsed = JSON.parse(savedState);
      if (parsed && Array.isArray(parsed.products) && Array.isArray(parsed.pending)) {
        state = { ...defaultState, ...parsed };
      } else {
        showToast("ข้อมูลที่บันทึกไว้ไม่ครบถ้วน ระบบใช้ข้อมูลตัวอย่างแทน", true);
      }
    }
  } catch (error) {
    console.error("Could not load auction data from local storage.", error);
    showToast("ไม่สามารถอ่านข้อมูลที่บันทึกไว้ได้ ระบบใช้ข้อมูลตัวอย่างแทน", true);
  }

  function persist() {
    try {
      window.localStorage.setItem(storageKey, JSON.stringify(state));
    } catch (error) {
      console.error("Could not save auction data to local storage.", error);
      showToast("บันทึกข้อมูลในเครื่องไม่สำเร็จ กรุณาตรวจสอบพื้นที่จัดเก็บของเบราว์เซอร์", true);
    }
  }

  function escapeHtml(value) {
    return String(value ?? "").replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]);
  }

  function safeImageUrl(value) {
    try {
      const url = new URL(value, window.location.origin);
      return url.protocol === "https:" || url.protocol === "http:" ? url.href : imageFallback;
    } catch {
      return imageFallback;
    }
  }

  function money(value) {
    return `฿${Number(value).toLocaleString("th-TH")}`;
  }

  function remainingTime(seconds) {
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    const remaining = seconds % 60;
    return hours > 24
      ? `${Math.floor(hours / 24)} วัน ${hours % 24} ชม.`
      : `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}:${String(remaining).padStart(2, "0")}`;
  }

  function renderProducts() {
    const grid = document.getElementById("productGrid");
    let products = state.products.filter(product => activeCategory === "all" || product.category === activeCategory);
    products = products.filter(product => `${product.name} ${product.category} ${product.condition}`.toLowerCase().includes(searchTerm));
    products.sort((a, b) => sortMode === "newest" ? String(b.id).localeCompare(String(a.id)) : a.endsIn - b.endsIn);
    grid.innerHTML = products.map(product => {
      const discount = categoryDiscounts[product.category];
      const saved = state.saved.includes(product.id);
      return `<article class="product-card">
        <div class="product-image" data-detail="${escapeHtml(product.id)}">
          <img src="${safeImageUrl(product.image)}" alt="${escapeHtml(product.name)}" loading="lazy" onerror="this.src='${imageFallback}'">
          <span class="product-badge">กำลังประมูล</span>
          ${discount ? `<span class="product-badge discount">ส่วนลดหมวด ${discount}%</span>` : ""}
          <button class="favorite-button${saved ? " saved" : ""}" data-save="${escapeHtml(product.id)}" aria-label="${saved ? "นำออกจากรายการที่สนใจ" : "บันทึกรายการที่สนใจ"}">${saved ? "♥" : "♡"}</button>
        </div>
        <div class="product-info">
          <p class="product-category">${escapeHtml(product.category)}</p>
          <h3 class="product-name" data-detail="${escapeHtml(product.id)}">${escapeHtml(product.name)}</h3>
          <p class="product-condition">${escapeHtml(product.condition)}</p><div class="product-divider"></div>
          <div class="price-row"><div><span class="price-label">ราคาสูงสุดปัจจุบัน</span><strong class="product-price">${money(product.price)}</strong></div><span class="bid-count">${product.bids} ราคา</span></div>
          <div class="card-bottom"><span class="countdown" data-countdown="${escapeHtml(product.id)}">${remainingTime(product.endsIn)}</span><button class="bid-button" data-detail="${escapeHtml(product.id)}">เสนอราคา ↗</button></div>
        </div>
      </article>`;
    }).join("");
    document.getElementById("liveCount").textContent = String(products.length).padStart(2, "0");
    document.getElementById("emptyState").classList.toggle("hidden", products.length > 0);
  }

  function renderSaved() {
    const savedProducts = state.products.filter(product => state.saved.includes(product.id));
    document.getElementById("savedGrid").innerHTML = savedProducts.map(product => {
      const discount = categoryDiscounts[product.category];
      return `<article class="product-card"><div class="product-image" data-detail="${escapeHtml(product.id)}"><img src="${safeImageUrl(product.image)}" alt="${escapeHtml(product.name)}" loading="lazy"><span class="product-badge">กำลังประมูล</span>${discount ? `<span class="product-badge discount">ส่วนลดหมวด ${discount}%</span>` : ""}</div><div class="product-info"><p class="product-category">${escapeHtml(product.category)}</p><h3 class="product-name" data-detail="${escapeHtml(product.id)}">${escapeHtml(product.name)}</h3><div class="price-row"><strong class="product-price">${money(product.price)}</strong><button class="bid-button" data-detail="${escapeHtml(product.id)}">เสนอราคา ↗</button></div></div></article>`;
    }).join("");
    document.getElementById("savedEmpty").classList.toggle("hidden", savedProducts.length > 0);
    const rows = state.bidHistory.slice().reverse().map(bid => `<tr><td>${escapeHtml(bid.name)}</td><td class="table-price">${money(bid.price)}</td><td><span class="status-pill ${bid.leading ? "status-green" : "status-pending"}">${bid.leading ? "ราคาสูงสุด" : "มีผู้เสนอสูงกว่า"}</span></td><td>${escapeHtml(bid.time)}</td></tr>`).join("");
    document.getElementById("bidHistoryRows").innerHTML = rows || '<tr class="empty-row"><td colspan="4">คุณยังไม่มีประวัติการเสนอราคา</td></tr>';
  }

  function renderSellerRows() {
    const rows = state.sellerItems.map(item => `<tr><td><span class="table-product"><img class="table-thumb" src="${safeImageUrl(item.image)}" alt=""><span><strong>${escapeHtml(item.name)}</strong><small>${escapeHtml(item.category)}</small></span></span></td><td>${money(item.price)}</td><td>${escapeHtml(item.startAt)} – ${escapeHtml(item.endAt)}</td><td><span class="status-pill status-pending">${escapeHtml(item.status)}</span></td></tr>`).join("");
    document.getElementById("sellerRows").innerHTML = rows || '<tr class="empty-row"><td colspan="4">ยังไม่มีสินค้าที่ลงขาย</td></tr>';
  }

  function renderAdminRows() {
    const rows = state.pending.map(item => `<tr><td><span class="table-product"><img class="table-thumb" src="${safeImageUrl(item.image)}" alt=""><span><strong>${escapeHtml(item.name)}</strong><small>${escapeHtml(item.category)}</small></span></span></td><td>${escapeHtml(item.seller)}</td><td class="table-price">${money(item.price)}</td><td>${escapeHtml(item.startAt)}<small class="table-subline">ถึง ${escapeHtml(item.endAt)}</small></td><td><div class="admin-review"><button class="review-button" data-approve="${escapeHtml(item.id)}">อนุมัติ</button><button class="review-button reject" data-reject="${escapeHtml(item.id)}">ปฏิเสธ</button></div></td></tr>`).join("");
    document.getElementById("adminRows").innerHTML = rows || '<tr class="empty-row"><td colspan="5">ไม่มีสินค้ารอตรวจสอบ</td></tr>';
    document.getElementById("pendingCount").textContent = String(state.pending.length).padStart(2, "0");
  }

  function renderWinners() {
    const rows = state.winners.slice().reverse().map((winner, index) => `<tr><td><span class="table-product"><img class="table-thumb" src="${safeImageUrl(winner.image)}" alt=""><span><strong>${escapeHtml(winner.name)}</strong><small>${escapeHtml(winner.category)} · #AUC-${String(state.winners.length - index).padStart(4, "0")}</small></span></span></td><td>${escapeHtml(winner.winner)}</td><td class="table-price">${money(winner.price)}</td><td>${escapeHtml(winner.date)}</td><td><span class="status-pill status-green">${escapeHtml(winner.status)}</span></td></tr>`).join("");
    document.getElementById("winnerRows").innerHTML = rows || '<tr class="empty-row"><td colspan="5">ยังไม่มีประวัติผู้ชนะ</td></tr>';
    document.getElementById("winnerCount").textContent = String(state.winners.length).padStart(2, "0");
    document.getElementById("closedCount").textContent = String(state.winners.length).padStart(2, "0");
  }

  function renderPayments() {
    const unpaid = state.payments.filter(payment => !payment.paid);
    document.getElementById("paymentList").innerHTML = unpaid.length ? unpaid.map(payment => {
      const discount = payment.discount || categoryDiscounts[payment.category] || 0;
      const total = Math.round(payment.price * (100 - discount) / 100);
      return `<article class="payment-card"><img class="payment-photo" src="${safeImageUrl(payment.image)}" alt="${escapeHtml(payment.name)}"><div class="payment-info"><small>${escapeHtml(payment.category)} · ผู้ขาย ${escapeHtml(payment.seller)}</small><h3>${escapeHtml(payment.name)}</h3><p>ส่วนลดหมวด ${discount}% · นัดรับกับผู้ขายหลังชำระเงิน</p></div><div class="payment-amount">${money(total)}</div><button class="button button-primary" data-pay="${escapeHtml(payment.id)}">ชำระเงิน <span>→</span></button></article>`;
    }).join("") : '<div class="empty-state">ไม่มีรายการค้างชำระในขณะนี้</div>';
  }

  function renderAll() {
    renderProducts();
    renderSaved();
    renderSellerRows();
    renderAdminRows();
    renderWinners();
    renderPayments();
  }

  function openProduct(productId) {
    const product = state.products.find(item => item.id === productId);
    if (!product) {
      showToast("ไม่พบสินค้านี้ในรายการประมูล", true);
      return;
    }
    selectedProductId = product.id;
    const history = (product.history || []).slice(0, 8).map(bid => `<div class="dialog-history-row"><span>${escapeHtml(bid.name)} · ${escapeHtml(bid.time)}</span><strong>${money(bid.price)}</strong></div>`).join("");
    const discount = categoryDiscounts[product.category];
    document.getElementById("dialogContent").innerHTML = `<div class="dialog-product"><img class="dialog-image" src="${safeImageUrl(product.image)}" alt="${escapeHtml(product.name)}"><div class="dialog-details"><p class="product-category">${escapeHtml(product.category)}${discount ? ` · ลด ${discount}% เมื่อชนะประมูล` : ""}</p><h2>${escapeHtml(product.name)}</h2><p class="product-condition">${escapeHtml(product.condition)} · ผู้ขาย ${escapeHtml(product.seller)}</p><p class="dialog-description">${escapeHtml(product.description)}</p><span class="dialog-current-label">ราคาสูงสุดปัจจุบัน</span><strong class="dialog-current-price">${money(product.price)}</strong><form class="dialog-bid-form" id="bidForm"><input name="bid" type="number" step="${product.increment}" min="${product.price + product.increment}" placeholder="ขั้นต่ำ ${money(product.price + product.increment)}" required><button type="submit">ยืนยันเสนอราคา</button></form><p class="dialog-minimum">เสนอเพิ่มขั้นต่ำ ${money(product.increment)} · เหลือเวลา ${remainingTime(product.endsIn)}</p><h3 class="dialog-history-title">ประวัติการเสนอราคา (${product.bids} ครั้ง)</h3><div class="dialog-history">${history || '<p class="dialog-minimum">ยังไม่มีผู้เสนอราคา</p>'}</div></div></div>`;
    document.getElementById("auctionDialog").showModal();
  }

  function switchView(view) {
    if (!pageTitles[view]) return;
    document.querySelectorAll(".view-panel").forEach(panel => panel.classList.toggle("active", panel.id === `view-${view}`));
    document.querySelectorAll("[data-view-link]").forEach(link => link.classList.toggle("active", link.dataset.viewLink === view));
    document.getElementById("pageCrumb").textContent = pageTitles[view];
    window.scrollTo({ top: 0, behavior: "smooth" });
  }

  document.querySelectorAll("[data-view-link]").forEach(link => link.addEventListener("click", event => {
    event.preventDefault();
    switchView(link.dataset.viewLink);
  }));

  document.querySelectorAll(".category-chip, [data-category='all'].text-button").forEach(button => button.addEventListener("click", () => {
    activeCategory = button.dataset.category || "all";
    document.querySelectorAll(".category-chip").forEach(chip => chip.classList.toggle("active", chip.dataset.category === activeCategory));
    renderProducts();
    if (!document.getElementById("view-today").classList.contains("active")) switchView("today");
  }));

  document.querySelectorAll(".sort-button[data-sort]").forEach(button => button.addEventListener("click", () => {
    sortMode = button.dataset.sort;
    document.querySelectorAll(".sort-button[data-sort]").forEach(item => item.classList.toggle("active", item === button));
    renderProducts();
  }));

  document.getElementById("searchInput").addEventListener("input", event => {
    searchTerm = event.target.value.trim().toLowerCase();
    renderProducts();
  });

  document.addEventListener("click", event => {
    const target = event.target;
    if (!(target instanceof Element)) return;
    const saveTarget = target.closest("[data-save]");
    if (saveTarget) {
      const id = saveTarget.dataset.save;
      state.saved = state.saved.includes(id) ? state.saved.filter(savedId => savedId !== id) : [...state.saved, id];
      persist();
      renderAll();
      return;
    }
    const detailTarget = target.closest("[data-detail]");
    if (detailTarget) {
      openProduct(detailTarget.dataset.detail);
      return;
    }
    const approvalTarget = target.closest("[data-approve]");
    if (approvalTarget) {
      const item = state.pending.find(pending => pending.id === approvalTarget.dataset.approve);
      if (!item) return;
      state.pending = state.pending.filter(pending => pending.id !== item.id);
      state.products.push({ ...item, id: `approved-${Date.now()}`, condition: "สินค้าสะสม · รอตรวจรายละเอียด", bids: 0, endsIn: 86300, increment: 50, description: "สินค้าผ่านการตรวจสอบจากแอดมินแล้ว โปรดสอบถามผู้ขายเพื่อดูรายละเอียดเพิ่มเติม", history: [] });
      persist();
      renderAll();
      showToast("อนุมัติสินค้าแล้ว และเพิ่มเข้าสู่รายการประมูล");
      return;
    }
    const rejectTarget = target.closest("[data-reject]");
    if (rejectTarget) {
      const item = state.pending.find(pending => pending.id === rejectTarget.dataset.reject);
      if (!item) return;
      state.pending = state.pending.filter(pending => pending.id !== item.id);
      persist();
      renderAdminRows();
      showToast(`ปฏิเสธรายการ “${item.name}” แล้ว`);
      return;
    }
    const payTarget = target.closest("[data-pay]");
    if (payTarget) {
      const payment = state.payments.find(item => item.id === payTarget.dataset.pay);
      if (!payment || payment.paid) return;
      payment.paid = true;
      state.winners.push({ id: `winner-${Date.now()}`, name: payment.name, category: payment.category, price: payment.price, winner: "คุณพิมพ์ชนก", date: new Intl.DateTimeFormat("th-TH", { day: "numeric", month: "short", year: "numeric" }).format(new Date()), status: "รอนัดรับ", image: payment.image });
      persist();
      renderAll();
      showToast("ชำระเงินสำเร็จแล้ว ไปที่กล่องข้อความเพื่อนัดรับสินค้า");
      return;
    }
    const scrollTarget = target.closest("[data-scroll]");
    if (scrollTarget) document.getElementById(scrollTarget.dataset.scroll)?.scrollIntoView({ behavior: "smooth", block: "start" });
    const linkedProduct = target.closest(".chat-item-link");
    if (linkedProduct) openProduct(linkedProduct.dataset.product);
  });

  document.getElementById("auctionDialog").addEventListener("submit", event => {
    if (event.target.id !== "bidForm") return;
    event.preventDefault();
    const product = state.products.find(item => item.id === selectedProductId);
    const bid = Number(new FormData(event.target).get("bid"));
    if (!product || !Number.isFinite(bid) || bid < product.price + product.increment) {
      showToast("ราคาเสนอใหม่ต้องสูงกว่าราคาปัจจุบันอย่างน้อยตามขั้นต่ำ", true);
      return;
    }
    product.price = bid;
    product.bids += 1;
    product.history = [{ name: "คุณพิมพ์ชนก", price: bid, time: new Intl.DateTimeFormat("th-TH", { hour: "2-digit", minute: "2-digit" }).format(new Date()) }, ...(product.history || [])];
    state.bidHistory.push({ id: product.id, name: product.name, price: bid, leading: true, time: new Intl.DateTimeFormat("th-TH", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" }).format(new Date()) });
    persist();
    renderAll();
    openProduct(product.id);
    showToast(`ส่งราคา ${money(bid)} สำหรับ “${product.name}” แล้ว`);
  });

  document.getElementById("listingForm").addEventListener("submit", event => {
    event.preventDefault();
    const form = event.currentTarget;
    if (!form.reportValidity()) return;
    const fields = new FormData(form);
    const start = new Date(fields.get("startAt"));
    const end = new Date(fields.get("endAt"));
    if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || end <= start) {
      showToast("เวลาสิ้นสุดประมูลต้องอยู่หลังเวลาเริ่มประมูล", true);
      return;
    }
    const formatDate = date => new Intl.DateTimeFormat("th-TH", { day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit" }).format(date);
    const item = {
      id: `seller-${Date.now()}`,
      name: String(fields.get("name")).trim(),
      category: String(fields.get("category")),
      condition: String(fields.get("condition")),
      description: String(fields.get("description")).trim(),
      image: safeImageUrl(String(fields.get("image") || "")),
      price: Number(fields.get("startingPrice")),
      increment: Number(fields.get("increment")) || 50,
      bids: 0,
      endsIn: Math.max(60, Math.floor((end.getTime() - Date.now()) / 1000)),
      startAt: formatDate(start),
      endAt: formatDate(end),
      seller: "คุณพิมพ์ชนก",
      status: "รอตรวจสอบ",
      history: []
    };
    if (item.price <= 0 || !Number.isFinite(item.price)) {
      showToast("กรุณาระบุราคาเริ่มต้นที่มากกว่า 0 บาท", true);
      return;
    }
    state.sellerItems.push(item);
    state.pending.push(item);
    persist();
    renderAll();
    form.reset();
    switchView("admin");
    showToast("ส่งสินค้าให้แอดมินตรวจสอบแล้ว");
  });

  document.getElementById("chatForm").addEventListener("submit", event => {
    event.preventDefault();
    const input = document.getElementById("chatInput");
    const message = input.value.trim();
    if (!message) return;
    const bubble = document.createElement("div");
    bubble.className = "chat-bubble sent";
    bubble.textContent = message;
    const time = document.createElement("small");
    time.textContent = new Intl.DateTimeFormat("th-TH", { hour: "2-digit", minute: "2-digit" }).format(new Date());
    bubble.append(time);
    document.getElementById("chatMessages").append(bubble);
    input.value = "";
    document.getElementById("chatMessages").scrollTop = document.getElementById("chatMessages").scrollHeight;
    showToast("ส่งข้อความแล้ว");
  });

  document.getElementById("todayDate").textContent = new Intl.DateTimeFormat("th-TH", { weekday: "long", day: "numeric", month: "long", year: "numeric" }).format(new Date());
  document.querySelectorAll("[name='startAt'], [name='endAt']").forEach(input => {
    const date = new Date(Date.now() - new Date().getTimezoneOffset() * 60000);
    input.value = new Date(date.getTime() + (input.name === "endAt" ? 86400000 : 3600000)).toISOString().slice(0, 16);
  });
  renderAll();

  window.setInterval(() => {
    state.products.forEach(product => { if (product.endsIn > 0) product.endsIn -= 1; });
    document.querySelectorAll("[data-countdown]").forEach(element => {
      const product = state.products.find(item => item.id === element.dataset.countdown);
      if (product) element.textContent = product.endsIn > 0 ? remainingTime(product.endsIn) : "ปิดประมูลแล้ว";
    });
  }, 1000);
})();
