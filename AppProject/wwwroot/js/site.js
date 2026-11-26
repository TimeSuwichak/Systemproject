(() => {
  const imageFallback = "https://images.unsplash.com/photo-1577083552431-6e5fd01aa342?auto=format&fit=crop&w=800&q=80";

  const roleLabels = {
    Buyer: "ผู้ประมูล",
    Seller: "ผู้ขาย",
    Admin: "แอดมิน",
    Finance: "การเงิน"
  };

  const pageTitles = {
    today: "ประมูลวันนี้",
    mybids: "รายการที่สนใจ & ประวัติบิด",
    payments: "ชำระเงิน & นัดรับ",
    "apply-seller": "สมัครเป็นผู้ขาย",
    messages: "กล่องข้อความ",
    sell: "ลงขาย & สินค้าของฉัน",
    "seller-orders": "นัดรับ & ยอดขาย",
    "admin-approvals": "อนุมัติผู้ขาย & สินค้า",
    "admin-users": "จัดการบัญชีผู้ใช้",
    "admin-categories": "จัดการหมวดหมู่สินค้า",
    "admin-disputes": "รับเรื่องข้อพิพาท",
    "admin-winners": "ประวัติผู้ชนะ (อ่านอย่างเดียว)",
    "finance-payments": "จัดการชำระเงิน / ปล่อยเงิน / คืนเงิน",
    "finance-rules": "กติกาส่วนลด & ค่าธรรมเนียม",
    "finance-report": "ประวัติผู้ชนะ & รายงานยอดขาย",
    "audit-logs": "บันทึกประวัติการทำงาน (Audit Log)"
  };

  let currentUser = null;
  let globalSettings = { sameCategoryDiscountPercent: 10, platformFeePercent: 5, globalMinBidIncrement: 50 };
  let activeView = "today";
  let activeCategoryId = 0;
  let sortMode = "ending";
  let searchTerm = "";
  let listViewMode = false;
  let liveProducts = [];
  let cachedCategories = [];
  let sellerListingsCache = [];
  let buyerOrdersCache = [];
  let buyerPaymentConfig = { platformPromptPayId: "081-999-8888", platformAccountName: "บจก. ของเก่าเล่าเรื่อง (บัญชีกลาง)" };
  let conversationsCache = [];
  let activeConversationId = null;
  let selectedProductId = null;
  let demoAccountsCache = [];
  let unreadMessagesCount = 0;
  let lastNotifiedMessageId = null;
  let notifPollTimer = null;

  const toastRegion = document.getElementById("toastRegion");
  const auctionDialog = document.getElementById("auctionDialog");
  const authDialog = document.getElementById("authDialog");
  const actionDialog = document.getElementById("actionDialog");

  function showToast(message, isError = false, onClick = null) {
    const toast = document.createElement("div");
    toast.className = `toast${isError ? " error" : ""}${onClick ? " clickable" : ""}`;
    toast.textContent = message;
    if (onClick) {
      toast.addEventListener("click", () => {
        toast.remove();
        onClick();
      });
    }
    toastRegion.append(toast);
    window.setTimeout(() => toast.remove(), onClick ? 6000 : 3800);
  }

  function escapeHtml(value) {
    return String(value ?? "").replace(/[&<>"']/g, ch => ({
      "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
    })[ch]);
  }

  function safeImageUrl(value) {
    try {
      const url = new URL(value || "", window.location.origin);
      return url.protocol === "https:" || url.protocol === "http:" ? url.href : imageFallback;
    } catch {
      return imageFallback;
    }
  }

  function money(value) {
    return `฿${Number(value || 0).toLocaleString("th-TH", { maximumFractionDigits: 2 })}`;
  }

  function parseUtcDate(iso) {
    if (!iso) return new Date(NaN);
    const s = String(iso).trim();
    const hasTz = /(?:Z|[+-]\d{2}:\d{2})$/i.test(s);
    return new Date(hasTz ? s : `${s}Z`);
  }

  function formatDateTime(iso) {
    if (!iso) return "-";
    const d = parseUtcDate(iso);
    if (Number.isNaN(d.getTime())) return "-";
    return new Intl.DateTimeFormat("th-TH", {
      day: "numeric", month: "short", year: "2-digit", hour: "2-digit", minute: "2-digit"
    }).format(d);
  }

  function formatDateShort(iso) {
    if (!iso) return "-";
    const d = parseUtcDate(iso);
    if (Number.isNaN(d.getTime())) return "-";
    return new Intl.DateTimeFormat("th-TH", { day: "numeric", month: "short", year: "2-digit" }).format(d);
  }

  function remainingText(endAtIso) {
    const diffSec = Math.floor((parseUtcDate(endAtIso).getTime() - Date.now()) / 1000);
    if (Number.isNaN(diffSec) || diffSec <= 0) return "ปิดประมูลแล้ว";
    const hours = Math.floor(diffSec / 3600);
    const minutes = Math.floor((diffSec % 3600) / 60);
    const secs = diffSec % 60;
    if (hours >= 24) return `${Math.floor(hours / 24)} วัน ${hours % 24} ชม.`;
    return `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}:${String(secs).padStart(2, "0")}`;
  }

  async function api(url, options = {}) {
    const headers = { ...(options.headers || {}) };
    if (options.body && !(options.body instanceof FormData)) {
      headers["Content-Type"] = "application/json";
    }
    const res = await fetch(url, { ...options, headers, credentials: "same-origin" });
    const text = await res.text();
    let data = null;
    try { data = text ? JSON.parse(text) : null; } catch { data = null; }
    if (!res.ok) {
      const msg = data?.message || `เกิดข้อผิดพลาด (${res.status})`;
      throw new Error(msg);
    }
    return data;
  }

  function openDialogSafe(dialog) {
    if (!dialog.open) dialog.showModal();
  }

  function closeMobileDrawer() {
    document.getElementById("sidebar").classList.remove("mobile-open");
    document.getElementById("sidebarBackdrop").classList.add("hidden");
  }

  function statusPill(type, text) {
    const map = {
      green: "status-green",
      pending: "status-pending",
      red: "status-red",
      blue: "status-blue",
      muted: "status-muted"
    };
    return `<span class="status-pill ${map[type] || "status-muted"}">${escapeHtml(text)}</span>`;
  }

  function meetupPill(status) {
    switch (status) {
      case "Completed": return statusPill("green", "ผู้ซื้อยืนยันรับของแล้ว");
      case "Delivered": return statusPill("blue", "ผู้ขายส่งมอบแล้ว");
      case "Scheduled": return statusPill("pending", "กำหนดนัดรับแล้ว");
      default: return statusPill("muted", "ยังไม่กำหนดนัดรับ");
    }
  }

  function paymentMethodLabel(method) {
    return method === "DirectToSeller" ? "จ่ายตรงผู้ขาย" : "QR ระบบกลาง (Escrow)";
  }

  function paymentPill(status, method) {
    switch (status) {
      case "Paid": return statusPill("green", "ยืนยันชำระเงินแล้ว");
      case "PendingVerification":
        return statusPill("blue", method === "DirectToSeller" ? "รอผู้ขายยืนยันรับเงิน" : "รอฝ่ายการเงินตรวจสลิป");
      case "Refunded": return statusPill("red", "คืนเงินแล้ว");
      case "Cancelled": return statusPill("muted", "หมดเวลาชำระเงิน / ยกเลิก");
      default: return statusPill("pending", "รอชำระเงิน");
    }
  }

  function payoutPill(status) {
    switch (status) {
      case "Released": return statusPill("green", "ปล่อยเงินให้ผู้ขายแล้ว");
      case "DirectSettled": return statusPill("blue", "ผู้ขายรับเงินตรงแล้ว");
      case "Refunded": return statusPill("red", "คืนเงินผู้ซื้อแล้ว");
      case "Cancelled": return statusPill("muted", "ยกเลิกแล้ว");
      default: return statusPill("pending", "รอปล่อยเงิน");
    }
  }

  function buildPromptPayQrSvg(seedText) {
    const size = 21;
    let hash = 2166136261;
    for (let i = 0; i < seedText.length; i++) {
      hash ^= seedText.charCodeAt(i);
      hash = Math.imul(hash, 16777619) >>> 0;
    }
    const isFinder = (r, c) =>
      (r < 7 && c < 7) || (r < 7 && c >= size - 7) || (r >= size - 7 && c < 7);
    const finderBit = (r, c) => {
      const lr = r < 7 ? r : r - (size - 7);
      const lc = c < 7 ? c : c - (size - 7);
      if (lr === 0 || lr === 6 || lc === 0 || lc === 6) return true;
      if (lr >= 2 && lr <= 4 && lc >= 2 && lc <= 4) return true;
      return false;
    };
    let rects = "";
    for (let r = 0; r < size; r++) {
      for (let c = 0; c < size; c++) {
        let on = false;
        if (isFinder(r, c)) {
          on = finderBit(r, c);
        } else if (r === 6 || c === 6) {
          on = (r + c) % 2 === 0;
        } else {
          hash = (Math.imul(hash ^ (r * 31 + c), 1103515245) + 12345) >>> 0;
          on = ((hash >> 16) & 1) === 1;
        }
        if (on) rects += `<rect x="${c}" y="${r}" width="1" height="1" fill="#182a20"/>`;
      }
    }
    return `<svg viewBox="0 0 ${size} ${size}" xmlns="http://www.w3.org/2000/svg" role="img" aria-label="PromptPay QR"><rect width="${size}" height="${size}" fill="#fff"/>${rects}</svg>`;
  }

  function openSlipModal(slipUrl, title, note = "") {
    document.getElementById("actionDialogContent").innerHTML = `
      <h2 class="auth-title">${escapeHtml(title)}</h2>
      ${note ? `<p class="auth-subtitle">หมายเหตุผู้ซื้อ: ${escapeHtml(note)}</p>` : ""}
      <img class="slip-full-img" src="${safeImageUrl(slipUrl)}" alt="หลักฐานการโอนเงิน">
      <div class="table-actions" style="justify-content:flex-end;">
        <a class="button button-secondary" href="${safeImageUrl(slipUrl)}" target="_blank" rel="noopener">เปิดภาพขนาดเต็ม ↗</a>
        <button type="button" class="button button-primary" data-close-action="1">ปิด</button>
      </div>
    `;
    openDialogSafe(actionDialog);
  }

  function openCheckoutModal(order) {
    const qrSeed = `${buyerPaymentConfig.platformPromptPayId}|${order.orderCode}|${order.finalBuyerAmount}`;
    document.getElementById("actionDialogContent").innerHTML = `
      <h2 class="auth-title">ชำระเงิน · ${escapeHtml(order.orderCode)}</h2>
      <p class="auth-subtitle">${escapeHtml(order.itemName)} · ยอดสุทธิ <strong>${money(order.finalBuyerAmount)}</strong>${order.discountAmount > 0 ? ` (รับส่วนลดหมวดเดียวกัน -${money(order.discountAmount)})` : ""}</p>
      ${order.paymentRejectReason ? `<div class="info-banner banner-error"><strong>ครั้งก่อนไม่ผ่านการตรวจสอบ</strong>เหตุผล: ${escapeHtml(order.paymentRejectReason)} — กรุณาตรวจสอบและส่งใหม่อีกครั้ง</div>` : ""}
      <form id="checkoutPayForm" enctype="multipart/form-data">
        <div class="pay-method-grid">
          <label class="pay-method-card selected" data-pay-card="PlatformEscrow">
            <input type="radio" name="paymentMethod" value="PlatformEscrow" checked>
            <strong>QR ระบบกลาง (แบบ Shopee)</strong>
            <small>โอนผ่าน QR เข้าระบบกลาง + แนบสลิป ฝ่ายการเงินคุ้มครองเงินจนกว่าคุณจะยืนยันรับของ</small>
          </label>
          <label class="pay-method-card" data-pay-card="DirectToSeller">
            <input type="radio" name="paymentMethod" value="DirectToSeller">
            <strong>จ่ายตรงให้ผู้ขาย</strong>
            <small>โอนตรงเข้าบัญชีผู้ขายหรือนัดจ่ายเงินสดหน้างาน แล้วให้ผู้ขายกดยืนยันรับเงิน</small>
          </label>
        </div>

        <div id="checkoutEscrowBox" class="qr-panel">
          <div class="qr-card">
            <div class="qr-header-bar">THAI QR PROMPTPAY</div>
            ${buildPromptPayQrSvg(qrSeed)}
          </div>
          <div class="qr-details">
            <strong>${escapeHtml(buyerPaymentConfig.platformAccountName)}</strong>
            <p>พร้อมเพย์บัญชีกลาง: <strong>${escapeHtml(buyerPaymentConfig.platformPromptPayId)}</strong></p>
            <p>อ้างอิงรายการ: <strong>${escapeHtml(order.orderCode)}</strong></p>
            <div class="qr-amount-big">${money(order.finalBuyerAmount)}</div>
            <p style="margin-top:6px;color:var(--muted);">สแกนจ่ายแล้วแนบสลิปด้านล่าง ฝ่ายการเงินจะตรวจยืนยันให้ทันที</p>
          </div>
        </div>

        <div id="checkoutDirectBox" class="direct-panel hidden">
          <strong>ข้อมูลชำระตรงกับผู้ขาย: ${escapeHtml(order.sellerName)}</strong>
          <p>บัญชีรับเงินผู้ขาย: <strong>${escapeHtml(order.sellerBank || "สอบถามทางแชท")}</strong></p>
          <p>เบอร์ติดต่อ: ${escapeHtml(order.sellerPhone || "-")}</p>
          <p>ยอดที่ต้องชำระ: <strong>${money(order.finalBuyerAmount)}</strong> (แนบสลิปโอนตรง หรือระบุว่านัดจ่ายเงินสดหน้างานก็ได้)</p>
        </div>

        <label class="field-label" id="slipFieldLabel">แนบภาพสลิปโอนเงิน <span id="slipReqStar">*</span>
          <input type="file" name="slipFile" id="checkoutSlipInput" accept=".jpg,.jpeg,.png,.webp,image/*" required>
          <small class="field-hint" id="slipFieldHint">รองรับไฟล์ .jpg, .png, .webp ขนาดไม่เกิน 5 MB (บังคับสำหรับการชำระผ่าน QR ระบบกลาง)</small>
        </label>
        <div class="slip-preview-wrap hidden" id="slipPreviewWrap">
          <img id="slipPreviewImg" class="slip-preview-img" alt="ตัวอย่างสลิป">
        </div>

        <label class="field-label" style="margin-top:12px;">หมายเหตุ / เวลาโอน / นัดจ่ายเงิน
          <input name="note" maxlength="280" placeholder="เช่น โอนจากกสิกร เวลา 23:15 หรือ นัดจ่ายเงินสดตอนรับของ">
        </label>

        <div class="table-actions" style="justify-content:flex-end;">
          <button type="button" class="button button-secondary" data-close-action="1">ยกเลิก</button>
          <button type="submit" class="button button-primary" id="checkoutSubmitBtn">ส่งสลิปให้ฝ่ายการเงินตรวจ</button>
        </div>
      </form>
    `;

    const form = document.getElementById("checkoutPayForm");
    const slipInput = document.getElementById("checkoutSlipInput");
    const previewWrap = document.getElementById("slipPreviewWrap");
    const previewImg = document.getElementById("slipPreviewImg");

    form.querySelectorAll("input[name='paymentMethod']").forEach(radio => {
      radio.addEventListener("change", () => {
        const isEscrow = radio.value === "PlatformEscrow";
        form.querySelectorAll(".pay-method-card").forEach(card => {
          card.classList.toggle("selected", card.dataset.payCard === radio.value);
        });
        document.getElementById("checkoutEscrowBox").classList.toggle("hidden", !isEscrow);
        document.getElementById("checkoutDirectBox").classList.toggle("hidden", isEscrow);
        slipInput.required = isEscrow;
        document.getElementById("slipReqStar").classList.toggle("hidden", !isEscrow);
        document.getElementById("slipFieldHint").textContent = isEscrow
          ? "รองรับไฟล์ .jpg, .png, .webp ขนาดไม่เกิน 5 MB (บังคับสำหรับการชำระผ่าน QR ระบบกลาง)"
          : "ไม่บังคับแนบสลิปหากนัดจ่ายเงินสดหน้างาน (ถ้าโอนตรงสามารถแนบสลิปให้ผู้ขายตรวจได้)";
        document.getElementById("checkoutSubmitBtn").textContent = isEscrow
          ? "ส่งสลิปให้ฝ่ายการเงินตรวจ"
          : "แจ้งชำระเงินตรงให้ผู้ขายยืนยัน";
      });
    });

    slipInput.addEventListener("change", () => {
      const file = slipInput.files?.[0];
      if (file) {
        previewImg.src = URL.createObjectURL(file);
        previewWrap.classList.remove("hidden");
      } else {
        previewWrap.classList.add("hidden");
      }
    });

    form.addEventListener("submit", async e => {
      e.preventDefault();
      const fd = new FormData(form);
      try {
        const res = await api(`/api/buyer/orders/${order.id}/pay`, {
          method: "POST",
          body: fd
        });
        actionDialog.close();
        showToast(res.message);
        await loadBuyerDashboard();
      } catch (err) {
        showToast(err.message, true);
      }
    });

    openDialogSafe(actionDialog);
  }

  function auctionStatusPill(status) {
    switch (status) {
      case "Approved": return statusPill("green", "เปิดประมูล");
      case "Pending": return statusPill("pending", "รอแอดมินอนุมัติ");
      case "Rejected": return statusPill("red", "ถูกปฏิเสธ");
      case "Closed": return statusPill("muted", "ปิดประมูลแล้ว");
      default: return statusPill("muted", status);
    }
  }

  function initListingDates() {
    const now = new Date();
    const toLocalInput = d => new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
    const startInput = document.querySelector("#listingForm [name='startAt']");
    const endInput = document.querySelector("#listingForm [name='endAt']");
    if (startInput) startInput.value = toLocalInput(new Date(now.getTime() + 5 * 60000));
    if (endInput) endInput.value = toLocalInput(new Date(now.getTime() + 24 * 3600000));
  }

  // ==================== NAVIGATION & AUTH UI ====================
  function buildSidebar() {
    const container = document.getElementById("sidebarNavContainer");
    const role = currentUser?.role;
    const sections = [];

    if (!role || role === "Buyer") {
      const mainItems = [{ view: "today", icon: "⌂", label: "ประมูลวันนี้" }];
      if (role === "Buyer") {
        mainItems.push(
          { view: "mybids", icon: "♡", label: "รายการที่สนใจ & ประวัติบิด" },
          { view: "payments", icon: "▣", label: "ชำระเงิน & นัดรับ" },
          { view: "messages", icon: "▤", label: "กล่องข้อความ" },
          { view: "apply-seller", icon: "＋", label: "สมัครเป็นผู้ขาย" }
        );
      }
      sections.push({ title: "ผู้ประมูล (Buyer)", items: mainItems });
    } else if (role === "Seller") {
      sections.push({
        title: "ผู้ขาย (Seller)",
        items: [
          { view: "sell", icon: "＋", label: "ลงขาย & สินค้าของฉัน" },
          { view: "seller-orders", icon: "▣", label: "นัดรับ & ยอดขาย" },
          { view: "messages", icon: "▤", label: "กล่องข้อความ" }
        ]
      });
      sections.push({
        title: "ประมูลซื้อของสะสม",
        items: [
          { view: "today", icon: "⌂", label: "ตลาดประมูลวันนี้" },
          { view: "mybids", icon: "♡", label: "รายการที่สนใจ & ประวัติบิด" },
          { view: "payments", icon: "฿", label: "ชำระเงิน & นัดรับ (ฝั่งผู้ซื้อ)" }
        ]
      });
    } else if (role === "Admin") {
      sections.push({
        title: "ผู้ดูแลระบบ (Admin)",
        items: [
          { view: "admin-approvals", icon: "◈", label: "อนุมัติผู้ขาย & สินค้า" },
          { view: "admin-users", icon: "♙", label: "จัดการบัญชีผู้ใช้" },
          { view: "admin-categories", icon: "✦", label: "จัดการหมวดหมู่สินค้า" },
          { view: "admin-disputes", icon: "⚑", label: "รับเรื่องข้อพิพาท" },
          { view: "admin-winners", icon: "▦", label: "ประวัติผู้ชนะ (อ่านอย่างเดียว)" },
          { view: "audit-logs", icon: "☰", label: "บันทึกการทำงาน (Audit Log)" },
          { view: "today", icon: "⌂", label: "ดูหน้าร้านประมูล" }
        ]
      });
    } else if (role === "Finance") {
      sections.push({
        title: "ฝ่ายการเงิน (Finance)",
        items: [
          { view: "finance-payments", icon: "฿", label: "ชำระเงิน / ปล่อยเงิน / คืนเงิน" },
          { view: "finance-rules", icon: "⚙", label: "ตั้งกติกาส่วนลด & ค่าธรรมเนียม" },
          { view: "finance-report", icon: "▤", label: "ประวัติผู้ชนะ & รายงานยอดขาย" },
          { view: "audit-logs", icon: "☰", label: "บันทึกการทำงาน (Audit Log)" },
          { view: "today", icon: "⌂", label: "ดูหน้าร้านประมูล" }
        ]
      });
    }

    container.innerHTML = sections.map(sec => `
      <div class="sidebar-label">${escapeHtml(sec.title)}</div>
      <nav class="side-nav">
        ${sec.items.map(it => `
          <button class="nav-item${activeView === it.view ? " active" : ""}" type="button" data-view-link="${it.view}">
            <span class="nav-icon">${it.icon}</span> ${escapeHtml(it.label)}
            ${it.view === "messages" ? `<span class="nav-count warn${unreadMessagesCount > 0 ? "" : " hidden"}" id="navUnreadMessagesBadge">${unreadMessagesCount}</span>` : ""}
          </button>
        `).join("")}
      </nav>
    `).join("");

    const sidebarAuth = document.getElementById("sidebarAuthArea");
    const topbarUser = document.getElementById("topbarUserArea");
    const heroAuthBtn = document.getElementById("heroAuthBtn");

    if (currentUser) {
      const initial = (currentUser.displayName || "?").trim().charAt(0);
      const roleLabel = roleLabels[currentUser.role] || currentUser.role;
      sidebarAuth.innerHTML = `
        <button class="profile-button" type="button" data-open-auth="1" title="สลับบัญชี">
          <span class="avatar">${escapeHtml(initial)}</span>
          <span><strong>${escapeHtml(currentUser.displayName)}</strong><small>${escapeHtml(roleLabel)} · สลับบัญชี</small></span>
          <span class="more">⇄</span>
        </button>
      `;
      topbarUser.innerHTML = `
        <span class="role-badge role-${escapeHtml(currentUser.role)}">${escapeHtml(roleLabel)}</span>
        <button class="button button-secondary button-sm" type="button" data-open-change-pw="1">เปลี่ยนรหัสผ่าน</button>
        <button class="button button-secondary button-sm" type="button" data-open-auth="1">สลับบัญชี</button>
        <button class="button button-danger button-sm" type="button" id="logoutBtn">ออกจากระบบ</button>
      `;
      if (heroAuthBtn) heroAuthBtn.textContent = `สลับบทบาท (${roleLabel})`;
    } else {
      sidebarAuth.innerHTML = `
        <button class="profile-button" type="button" data-open-auth="1">
          <span class="avatar">?</span>
          <span><strong>เข้าสู่ระบบ</strong><small>เลือก 4 บทบาทหรือสมัครใหม่</small></span>
          <span class="more">→</span>
        </button>
      `;
      topbarUser.innerHTML = `<button class="button button-primary button-sm" type="button" data-open-auth="1">เข้าสู่ระบบ / สมัครสมาชิก</button>`;
      if (heroAuthBtn) heroAuthBtn.textContent = "เข้าสู่ระบบ / ทดลอง 4 บทบาท";
    }
  }

  async function switchView(view) {
    if (!pageTitles[view]) return;
    activeView = view;
    closeMobileDrawer();

    document.querySelectorAll(".view-panel").forEach(p => p.classList.toggle("active", p.id === `view-${view}`));
    document.querySelectorAll("[data-view-link]").forEach(btn => btn.classList.toggle("active", btn.dataset.viewLink === view));
    document.getElementById("pageCrumb").textContent = pageTitles[view];
    document.getElementById("globalSearchBox").classList.toggle("hidden", view !== "today");
    window.scrollTo({ top: 0, behavior: "smooth" });

    try {
      if (view === "today") await loadAuctions();
      else if (view === "mybids" || view === "payments") await loadBuyerDashboard();
      else if (view === "apply-seller") renderBuyerApplySeller();
      else if (view === "messages") await loadConversations();
      else if (view === "sell" || view === "seller-orders") await loadSellerDashboard();
      else if (view.startsWith("admin-")) await loadAdminDashboard();
      else if (view.startsWith("finance-")) await loadFinanceDashboard();
      else if (view === "audit-logs") await loadAuditLogs();
    } catch (err) {
      showToast(err.message, true);
    }
  }

  // ==================== AUTH MODAL ====================
  async function openAuthModal(tab = "login") {
    document.querySelectorAll(".auth-tab").forEach(t => t.classList.toggle("active", t.dataset.authTab === tab));
    document.getElementById("authTabLogin").classList.toggle("hidden", tab !== "login");
    document.getElementById("authTabRegister").classList.toggle("hidden", tab !== "register");
    document.getElementById("authTabForgot")?.classList.toggle("hidden", tab !== "forgot");

    const demoSection = document.getElementById("demoAccountsSection");
    if (demoAccountsCache.length === 0) {
      try { demoAccountsCache = await api("/api/auth/demo-accounts"); } catch { demoAccountsCache = []; }
    }
    if (demoSection) {
      demoSection.classList.toggle("hidden", demoAccountsCache.length === 0);
    }
    document.getElementById("demoAccountsGrid").innerHTML = demoAccountsCache.map(acc => `
      <button type="button" class="demo-card" data-demo-email="${escapeHtml(acc.email)}">
        <span class="role-badge role-${escapeHtml(acc.role)}">${escapeHtml(acc.label)}</span>
        <strong>${escapeHtml(acc.name)}</strong>
        <small>${escapeHtml(acc.note)}</small>
      </button>
    `).join("");

    openDialogSafe(authDialog);
  }

  async function loadAuditLogs(roleFilter = "all") {
    const logs = await api(`/api/audit-logs?role=${encodeURIComponent(roleFilter)}`);
    document.getElementById("auditLogRows").innerHTML = logs.length
      ? logs.map(l => `
        <tr>
          <td style="white-space:nowrap;">${formatDateTime(l.createdAt)}</td>
          <td><strong>${escapeHtml(l.actorName)}</strong></td>
          <td><span class="role-badge role-${escapeHtml(l.actorRole)}">${escapeHtml(roleLabels[l.actorRole] || l.actorRole)}</span></td>
          <td><code>${escapeHtml(l.action)}</code></td>
          <td>${escapeHtml(l.targetType)}${l.targetId ? ` #${l.targetId}` : ""}</td>
          <td>${escapeHtml(l.details)}</td>
        </tr>`).join("")
      : `<tr class="empty-row"><td colspan="6">ยังไม่มีประวัติการทำงาน</td></tr>`;
  }

  async function refreshSession(goDefaultView = false) {
    const me = await api("/api/auth/me");
    const prevUserId = currentUser?.id || null;
    currentUser = me.authenticated ? me.user : null;
    if (me.settings) globalSettings = me.settings;

    if ((currentUser?.id || null) !== prevUserId) {
      lastNotifiedMessageId = null;
      unreadMessagesCount = 0;
      activeConversationId = null;
    }

    const notifWrapper = document.getElementById("notifWrapper");
    if (notifWrapper) notifWrapper.classList.toggle("hidden", !currentUser);
    document.getElementById("notifDropdown")?.classList.add("hidden");

    buildSidebar();

    if (notifPollTimer) {
      window.clearInterval(notifPollTimer);
      notifPollTimer = null;
    }

    if (goDefaultView) {
      const defaultByRole = {
        Buyer: "today",
        Seller: "sell",
        Admin: "admin-approvals",
        Finance: "finance-payments"
      };
      await switchView(currentUser ? (defaultByRole[currentUser.role] || "today") : "today");
    } else {
      await switchView(activeView);
    }

    if (currentUser) {
      await pollNotifications(true);
      notifPollTimer = window.setInterval(() => pollNotifications(false), 5000);
    }
  }

  async function pollNotifications(isInitial = false) {
    if (!currentUser) return;
    try {
      const data = await api("/api/notifications/summary");
      unreadMessagesCount = data.unreadMessagesCount || 0;

      // Update sidebar messages badge
      const navBadge = document.getElementById("navUnreadMessagesBadge");
      if (navBadge) {
        navBadge.textContent = unreadMessagesCount > 99 ? "99+" : String(unreadMessagesCount);
        navBadge.classList.toggle("hidden", unreadMessagesCount <= 0);
      }

      // Update topbar bell badge
      const bellBadge = document.getElementById("notifBadge");
      const total = data.totalCount || 0;
      if (bellBadge) {
        bellBadge.textContent = total > 99 ? "99+" : String(total);
        bellBadge.classList.toggle("hidden", total <= 0);
      }

      // Render dropdown items
      const listEl = document.getElementById("notifDropdownList");
      if (listEl) {
        const alertHtml = (data.roleAlerts || []).map(a => `
          <button type="button" class="notif-item unread" data-notif-view="${escapeHtml(a.view)}">
            <div class="notif-item-top"><strong><span class="notif-dot"></span>${escapeHtml(a.title)}</strong></div>
            <p>${escapeHtml(a.body)}</p>
          </button>`).join("");

        const notifHtml = (data.notifications || []).map(n => `
          <button type="button" class="notif-item${n.isRead ? "" : " unread"}"
            data-notif-id="${n.id}"
            data-notif-view="${escapeHtml(n.targetView || "")}"
            data-notif-ref="${n.referenceId || ""}">
            <div class="notif-item-top">
              <strong>${n.isRead ? "" : `<span class="notif-dot"></span>`}${escapeHtml(n.title)}</strong>
              <time>${formatDateTime(n.createdAt)}</time>
            </div>
            <p>${escapeHtml(n.body)}</p>
          </button>`).join("");

        listEl.innerHTML = (alertHtml + notifHtml) || `<div class="notif-empty">ไม่มีการแจ้งเตือนใหม่</div>`;
      }

      // Live incoming chat message notification + live refresh
      const latestId = data.latestMessageId || 0;
      const incoming = data.latestIncomingMessage;
      if (isInitial || lastNotifiedMessageId === null) {
        lastNotifiedMessageId = latestId;
      } else if (latestId > lastNotifiedMessageId && incoming) {
        lastNotifiedMessageId = latestId;
        if (activeView === "messages" && activeConversationId === incoming.conversationId) {
          await openConversation(incoming.conversationId, { preserveScroll: true });
        } else {
          const snippet = incoming.body.length > 60 ? `${incoming.body.slice(0, 60)}…` : incoming.body;
          showToast(`💬 ข้อความใหม่จาก ${incoming.senderName}: ${snippet}`, false, async () => {
            activeConversationId = incoming.conversationId;
            await switchView("messages");
          });
          if (activeView === "messages") {
            conversationsCache = await api("/api/chat/conversations");
            renderConversationList();
          }
        }
      }
    } catch {
      // Ignore transient poll errors
    }
  }

  // ==================== PUBLIC / BUYER: TODAY ====================
  async function loadAuctions() {
    const params = new URLSearchParams();
    if (activeCategoryId > 0) params.set("categoryId", String(activeCategoryId));
    if (searchTerm) params.set("search", searchTerm);
    params.set("sort", sortMode);

    const data = await api(`/api/auctions?${params.toString()}`);
    globalSettings = data.settings;
    cachedCategories = data.categories;
    liveProducts = data.products;

    document.getElementById("heroPromoText").textContent =
      `ชิ้นงานหายากจากนักสะสมทั่วไทย นัดรับได้จริง รับส่วนลด ${globalSettings.sameCategoryDiscountPercent}% อัตโนมัติเมื่อชนะสินค้าหมวดเดียวกัน`;
    document.getElementById("discountHintText").textContent =
      `กติกาปัจจุบัน: ซื้อสินค้าหมวดเดียวกันรับส่วนลด ${globalSettings.sameCategoryDiscountPercent}% · เสนอราคาเพิ่มขั้นต่ำ ${money(globalSettings.globalMinBidIncrement)}`;

    // Categories
    const catRow = document.getElementById("categoryRow");
    const allChip = `
      <button class="category-chip${activeCategoryId === 0 ? " active" : ""}" type="button" data-category="0">
        <span class="category-symbol">✦</span>
        <strong>ทั้งหมด</strong>
        <small>${data.totalLiveCount} รายการ</small>
      </button>`;
    const catChips = data.categories.map(c => `
      <button class="category-chip${activeCategoryId === c.id ? " active" : ""}" type="button" data-category="${c.id}">
        <span class="category-symbol">${escapeHtml(c.symbol || "✦")}</span>
        <strong>${escapeHtml(c.name)}</strong>
        <small class="${c.hasSameCategoryDiscount ? "discount-note" : ""}">
          ${c.hasSameCategoryDiscount ? `ลด ${globalSettings.sameCategoryDiscountPercent}% หมวดนี้ (${c.liveCount})` : `${c.liveCount} รายการ`}
        </small>
      </button>`).join("");
    catRow.innerHTML = allChip + catChips;

    // Product Grid
    const grid = document.getElementById("productGrid");
    grid.classList.toggle("list-view", listViewMode);
    grid.innerHTML = liveProducts.map(p => `
      <article class="product-card">
        <div class="product-image" data-detail="${p.id}">
          <img src="${safeImageUrl(p.imageUrl)}" alt="${escapeHtml(p.name)}" loading="lazy" onerror="this.src='${imageFallback}'">
          <span class="product-badge">${p.hasStarted ? "กำลังประมูล" : "เปิดเร็วๆ นี้"}</span>
          ${p.sameCategoryEligible && globalSettings.sameCategoryDiscountPercent > 0
            ? `<span class="product-badge discount">รับส่วนลดหมวดเดียวกัน ${globalSettings.sameCategoryDiscountPercent}%</span>` : ""}
          ${currentUser ? `<button class="favorite-button${p.saved ? " saved" : ""}" type="button" data-save="${p.id}" aria-label="บันทึกรายการที่สนใจ">${p.saved ? "♥" : "♡"}</button>` : ""}
        </div>
        <div class="product-info">
          <p class="product-category">${escapeHtml(p.categoryName)} · ${escapeHtml(p.sellerName)}</p>
          <h3 class="product-name" data-detail="${p.id}">${escapeHtml(p.name)}</h3>
          <p class="product-condition">${escapeHtml(p.condition)}</p>
          <div class="product-divider"></div>
          <div class="price-row">
            <div><span class="price-label">ราคาสูงสุดปัจจุบัน</span><strong class="product-price">${money(p.currentPrice)}</strong></div>
            <span class="bid-count">${p.bidCount} ราคา</span>
          </div>
          <div class="card-bottom">
            <span class="countdown" data-end-at="${escapeHtml(p.endAt)}">${remainingText(p.endAt)}</span>
            <button class="bid-button" type="button" data-detail="${p.id}">ดู & เสนอราคา ↗</button>
          </div>
        </div>
      </article>
    `).join("");

    document.getElementById("liveCount").textContent = String(liveProducts.length).padStart(2, "0");
    document.getElementById("emptyState").classList.toggle("hidden", liveProducts.length > 0);
  }

  async function openProduct(productId) {
    try {
      const p = await api(`/api/auctions/${productId}`);
      selectedProductId = p.id;

      const historyHtml = p.history.length
        ? p.history.map(b => `
          <div class="dialog-history-row">
            <span>${escapeHtml(b.bidderName)} · ${formatDateTime(b.createdAt)}</span>
            <strong>${money(b.amount)}</strong>
          </div>`).join("")
        : `<p class="dialog-minimum">ยังไม่มีผู้เสนอราคา เป็นคนแรกที่เริ่มประมูลได้เลย</p>`;

      let bidControlHtml = "";
      const isExpiredNow = p.isClosed || parseUtcDate(p.endAt).getTime() <= Date.now();
      const canBidRole = currentUser && (currentUser.role === "Buyer" || currentUser.role === "Seller");
      if (isExpiredNow) {
        bidControlHtml = `<div class="info-banner">รายการนี้ปิดประมูลแล้ว</div>`;
      } else if (!p.hasStarted) {
        bidControlHtml = `<div class="info-banner">เริ่มเปิดประมูลวันที่ ${formatDateTime(p.startAt)}</div>`;
      } else if (!currentUser) {
        bidControlHtml = `<button class="button button-primary full-width" type="button" data-open-auth="1">เข้าสู่ระบบเพื่อเสนอราคา</button>`;
      } else if (!canBidRole) {
        bidControlHtml = `<div class="info-banner">บัญชีปัจจุบัน (${escapeHtml(roleLabels[currentUser.role])}) สามารถดูรายละเอียดได้เท่านั้น</div>`;
      } else if (currentUser.id === p.sellerId) {
        bidControlHtml = `<div class="info-banner">นี่คือสินค้าของคุณ ไม่สามารถเสนอราคาสินค้าตัวเองได้</div>`;
      } else {
        bidControlHtml = `
          <form class="dialog-bid-form" id="bidForm" data-end-at="${escapeHtml(p.endAt)}">
            <input name="bid" type="number" step="1" min="${p.minNextBid}" value="${p.minNextBid}" required>
            <button type="submit">ยืนยันเสนอราคา</button>
          </form>
          <p class="dialog-minimum">เสนอขั้นต่ำ ${money(p.minNextBid)} (เพิ่มครั้งละ ≥ ${money(p.minIncrement)}) · เหลือเวลา <strong data-end-at="${escapeHtml(p.endAt)}">${remainingText(p.endAt)}</strong> · ต่อเวลาอัตโนมัติ 2 นาทีหากบิดช่วงท้าย</p>`;
      }

      const chatBtnHtml = canBidRole && currentUser.id !== p.sellerId
        ? `<button class="button button-secondary button-sm" type="button" data-start-chat="${p.id}">▤ แชทกับผู้ขาย</button>`
        : "";

      document.getElementById("dialogContent").innerHTML = `
        <div class="dialog-product">
          <img class="dialog-image" src="${safeImageUrl(p.imageUrl)}" alt="${escapeHtml(p.name)}" onerror="this.src='${imageFallback}'">
          <div class="dialog-details">
            <p class="product-category">${escapeHtml(p.categoryName)}${p.sameCategoryEligible ? ` · รับส่วนลดหมวดเดียวกัน ${p.sameCategoryDiscountPercent}%` : ""}</p>
            <h2>${escapeHtml(p.name)}</h2>
            <p class="product-condition">${escapeHtml(p.condition)} · ผู้ขาย ${escapeHtml(p.sellerName)}</p>
            <p class="dialog-description">${escapeHtml(p.description)}</p>
            <span class="dialog-current-label">ราคาสูงสุดปัจจุบัน (เริ่มต้น ${money(p.startPrice)})</span>
            <strong class="dialog-current-price">${money(p.currentPrice)}</strong>
            ${bidControlHtml}
            <div class="dialog-action-row">${chatBtnHtml}</div>
            <h3 class="dialog-history-title">ประวัติการเสนอราคา (${p.bidCount} ครั้ง)</h3>
            <div class="dialog-history">${historyHtml}</div>
          </div>
        </div>
      `;
      openDialogSafe(auctionDialog);
    } catch (err) {
      showToast(err.message, true);
    }
  }

  // ==================== BUYER DASHBOARD ====================
  async function loadBuyerDashboard() {
    const data = await api("/api/buyer/dashboard");

    // Saved grid
    const savedGrid = document.getElementById("savedGrid");
    savedGrid.innerHTML = data.saved.map(p => `
      <article class="product-card">
        <div class="product-image" data-detail="${p.id}">
          <img src="${safeImageUrl(p.imageUrl)}" alt="${escapeHtml(p.name)}" onerror="this.src='${imageFallback}'">
          <span class="product-badge">${p.status === "Approved" ? "กำลังประมูล" : "ปิดประมูล"}</span>
          <button class="favorite-button saved" type="button" data-save="${p.id}" aria-label="นำออก">♥</button>
        </div>
        <div class="product-info">
          <p class="product-category">${escapeHtml(p.categoryName)}</p>
          <h3 class="product-name" data-detail="${p.id}">${escapeHtml(p.name)}</h3>
          <div class="price-row" style="margin-top:8px;">
            <strong class="product-price">${money(p.currentPrice)}</strong>
            <button class="bid-button" type="button" data-detail="${p.id}">ดูรายละเอียด ↗</button>
          </div>
        </div>
      </article>
    `).join("");
    document.getElementById("savedEmpty").classList.toggle("hidden", data.saved.length > 0);

    // Bid history
    document.getElementById("bidHistoryRows").innerHTML = data.bidHistory.length
      ? data.bidHistory.map(b => `
        <tr>
          <td><a href="#" data-detail="${b.auctionItemId}" style="font-weight:600;color:var(--green);">${escapeHtml(b.itemName)}</a></td>
          <td>${escapeHtml(b.categoryName)}</td>
          <td class="table-price">${money(b.amount)}</td>
          <td>${money(b.currentPrice)}</td>
          <td>${statusPill(b.statusText === "ชนะประมูล" || b.statusText === "ราคาสูงสุด" ? "green" : b.isClosed ? "muted" : "pending", b.statusText)}</td>
          <td>${formatDateTime(b.createdAt)}</td>
        </tr>`).join("")
      : `<tr class="empty-row"><td colspan="6">คุณยังไม่มีประวัติการเสนอราคา</td></tr>`;

    // Cache buyer orders & payment config
    buyerOrdersCache = data.orders || [];
    if (data.paymentConfig) buyerPaymentConfig = data.paymentConfig;

    // Unpaid payments
    const unpaid = buyerOrdersCache.filter(o => o.paymentStatus === "Unpaid");
    document.getElementById("paymentList").innerHTML = unpaid.length
      ? unpaid.map(o => `
        <article class="payment-card">
          <img class="payment-photo" src="${safeImageUrl(o.imageUrl)}" alt="${escapeHtml(o.itemName)}" onerror="this.src='${imageFallback}'">
          <div class="payment-info">
            <small>${escapeHtml(o.orderCode)} · ${escapeHtml(o.categoryName)} · ผู้ขาย ${escapeHtml(o.sellerName)}</small>
            <h3>${escapeHtml(o.itemName)}</h3>
            <p>${o.discountAmount > 0 ? `<strong style="color:#966222;">✦ ${escapeHtml(o.discountReason)} (-${money(o.discountAmount)})</strong>` : "ไม่มีส่วนลดหมวดเดียวกัน (ชำระชิ้นนี้แล้วรับส่วนลดในชิ้นต่อไปหมวดเดียวกัน)"}</p>
            ${o.paymentDueAt ? `<p style="color:var(--rose);margin-top:3px;"><strong>กำหนดชำระภายใน:</strong> ${formatDateTime(o.paymentDueAt)}</p>` : ""}
            ${o.paymentRejectReason ? `<p style="color:var(--rose);margin-top:4px;"><strong>ถูกปฏิเสธครั้งก่อน:</strong> ${escapeHtml(o.paymentRejectReason)}</p>` : ""}
          </div>
          <div class="payment-pricing">
            ${o.discountAmount > 0 ? `<span class="payment-strike">${money(o.winningPrice)}</span>` : ""}
            <div class="payment-amount">${money(o.finalBuyerAmount)}</div>
          </div>
          <button class="button button-primary" type="button" data-pay-order="${o.id}">เลือกวิธีชำระเงิน <span>→</span></button>
        </article>`).join("")
      : `<div class="empty-state">ไม่มีรายการค้างชำระในขณะนี้</div>`;

    // All won orders & meetups
    document.getElementById("buyerOrdersRows").innerHTML = buyerOrdersCache.length
      ? buyerOrdersCache.map(o => `
        <tr>
          <td>
            <div class="table-product">
              <img class="table-thumb" src="${safeImageUrl(o.imageUrl)}" alt="" onerror="this.src='${imageFallback}'">
              <div><strong>${escapeHtml(o.itemName)}</strong><small>${escapeHtml(o.orderCode)} · ${escapeHtml(o.categoryName)}</small></div>
            </div>
          </td>
          <td>${escapeHtml(o.sellerName)}<small class="table-subline">${escapeHtml(o.sellerPhone || "")}</small></td>
          <td class="table-price">
            ${money(o.finalBuyerAmount)}
            ${o.discountAmount > 0 ? `<small class="table-subline" style="color:#966222;">ลด ${o.discountPercent}% (-${money(o.discountAmount)})</small>` : ""}
          </td>
          <td>
            ${paymentPill(o.paymentStatus, o.paymentMethod)}
            <small class="table-subline">${escapeHtml(paymentMethodLabel(o.paymentMethod))}</small>
            ${o.paymentRejectReason ? `<small class="table-subline" style="color:var(--rose);">เหตุผล: ${escapeHtml(o.paymentRejectReason)}</small>` : ""}
            ${o.financeNote ? `<small class="table-subline">${escapeHtml(o.financeNote)}</small>` : ""}
          </td>
          <td>
            ${meetupPill(o.meetupStatus)}
            ${o.meetupLocation ? `<small class="table-subline">⌖ ${escapeHtml(o.meetupLocation)} (${formatDateTime(o.meetupAt)})</small>` : ""}
          </td>
          <td>
            <div class="table-actions">
              ${o.paymentStatus === "Unpaid"
                ? `<button class="button button-primary button-sm" type="button" data-pay-order="${o.id}">ชำระเงิน</button>` : ""}
              ${o.paymentSlipUrl
                ? `<button class="button button-secondary button-sm" type="button" data-view-slip="${escapeHtml(o.paymentSlipUrl)}" data-slip-title="สลิป ${escapeHtml(o.orderCode)}" data-slip-note="${escapeHtml(o.paymentNote || "")}">ดูสลิป</button>` : ""}
              ${o.paymentStatus === "Paid" && o.meetupStatus !== "Completed"
                ? `<button class="button button-primary button-sm" type="button" data-confirm-received="${o.id}">ยืนยันรับสินค้าแล้ว</button>` : ""}
              ${o.conversationId
                ? `<button class="button button-secondary button-sm" type="button" data-open-conv="${o.conversationId}">แชท / แจ้งปัญหา</button>`
                : `<button class="button button-secondary button-sm" type="button" data-start-chat="${o.auctionItemId}">แชทผู้ขาย</button>`}
            </div>
          </td>
        </tr>`).join("")
      : `<tr class="empty-row"><td colspan="6">ยังไม่มีประวัติสินค้าที่ประมูลได้</td></tr>`;
  }

  function renderBuyerApplySeller() {
    const statusBox = document.getElementById("buyerApplySellerStatus");
    const form = document.getElementById("buyerApplySellerForm");
    if (!currentUser) return;

    if (currentUser.sellerStatus === "Pending") {
      statusBox.innerHTML = `<div class="info-banner"><strong>สถานะใบสมัครผู้ขาย: รอแอดมินตรวจสอบ</strong>ร้าน “${escapeHtml(currentUser.shopName)}” ส่งคำขอเมื่อ ${formatDateTime(currentUser.sellerAppliedAt)} เมื่อแอดมินอนุมัติ บัญชีของคุณจะได้รับสิทธิ์ผู้ขาย (กรุณาออกจากระบบแล้วเข้าใหม่เพื่อใช้งานเมนูผู้ขาย)</div>`;
      form.classList.add("hidden");
    } else if (currentUser.sellerStatus === "Rejected") {
      statusBox.innerHTML = `<div class="info-banner banner-error"><strong>ใบสมัครครั้งก่อนถูกปฏิเสธ</strong>เหตุผล: ${escapeHtml(currentUser.sellerRejectReason || "-")} — คุณสามารถแก้ไขข้อมูลและส่งสมัครใหม่ได้ด้านล่าง</div>`;
      form.classList.remove("hidden");
    } else {
      statusBox.innerHTML = "";
      form.classList.remove("hidden");
    }
  }

  // ==================== SELLER DASHBOARD ====================
  async function loadSellerDashboard() {
    if (cachedCategories.length === 0) {
      const pub = await api("/api/auctions");
      cachedCategories = pub.categories;
    }

    const data = await api("/api/seller/dashboard");
    sellerListingsCache = data.listings;
    const seller = data.seller;
    currentUser = seller;

    // Populate category dropdown & min increment hint
    const catSelect = document.getElementById("listingCategorySelect");
    catSelect.innerHTML = `<option value="">เลือกหมวดหมู่</option>` +
      cachedCategories.map(c => `<option value="${c.id}">${escapeHtml(c.name)}</option>`).join("");
    const minIncInput = document.getElementById("listingMinIncrement");
    minIncInput.min = data.globalMinBidIncrement;
    if (Number(minIncInput.value) < data.globalMinBidIncrement) minIncInput.value = data.globalMinBidIncrement;
    document.getElementById("sellerMinBidHint").textContent = `ขั้นต่ำตามกติกาฝ่ายการเงิน ≥ ${money(data.globalMinBidIncrement)} · ค่าธรรมเนียมระบบ ${data.platformFeePercent}%`;

    // Approval status banner
    const banner = document.getElementById("sellerApprovalBanner");
    const formWrap = document.getElementById("sellerListingFormWrap");
    const reapplyBox = document.getElementById("sellerReapplyBox");

    if (seller.sellerStatus === "Approved") {
      banner.innerHTML = `<div class="info-banner banner-success"><strong>ผู้ขายได้รับการอนุมัติแล้ว · ร้าน ${escapeHtml(seller.shopName || seller.displayName)}</strong>บัญชีรับเงิน: ${escapeHtml(seller.bankAccount || "-")}</div>`;
      formWrap.classList.remove("hidden");
      reapplyBox.classList.add("hidden");
    } else if (seller.sellerStatus === "Pending") {
      banner.innerHTML = `<div class="info-banner"><strong>ใบสมัครผู้ขายอยู่ระหว่างรอแอดมินอนุมัติ</strong>ร้าน: ${escapeHtml(seller.shopName || "-")} · เบอร์โทร: ${escapeHtml(seller.phone || "-")} — เมื่อแอดมินอนุมัติแล้วจะสามารถลงขายสินค้าได้ทันที</div>`;
      formWrap.classList.add("hidden");
      reapplyBox.classList.add("hidden");
    } else {
      banner.innerHTML = `<div class="info-banner banner-error"><strong>ใบสมัครผู้ขายถูกปฏิเสธ</strong>เหตุผล: ${escapeHtml(seller.sellerRejectReason || "กรุณาตรวจสอบข้อมูลและส่งสมัครอีกครั้ง")}</div>`;
      formWrap.classList.add("hidden");
      reapplyBox.classList.remove("hidden");
      const rf = document.getElementById("sellerReapplyForm");
      rf.shopName.value = seller.shopName || "";
      rf.phone.value = seller.phone || "";
      rf.bankAccount.value = seller.bankAccount || "";
      rf.sellerBio.value = seller.sellerBio || "";
    }

    // Seller listings table
    document.getElementById("sellerRows").innerHTML = data.listings.length
      ? data.listings.map(item => `
        <tr>
          <td>
            <div class="table-product">
              <img class="table-thumb" src="${safeImageUrl(item.imageUrl)}" alt="" onerror="this.src='${imageFallback}'">
              <div><strong>${escapeHtml(item.name)}</strong><small>${escapeHtml(item.categoryName)} · ${escapeHtml(item.condition)}</small></div>
            </div>
          </td>
          <td class="table-price">
            ${money(item.currentPrice)}
            <small class="table-subline">เริ่ม ${money(item.startPrice)} (+${money(item.minIncrement)})</small>
          </td>
          <td>${formatDateTime(item.startAt)}<small class="table-subline">ถึง ${formatDateTime(item.endAt)}</small></td>
          <td>
            ${auctionStatusPill(item.status)}
            ${item.rejectReason ? `<small class="table-subline" style="color:var(--rose);">เหตุผล: ${escapeHtml(item.rejectReason)}</small>` : ""}
            ${item.winnerName ? `<small class="table-subline">ผู้ชนะ: ${escapeHtml(item.winnerName)}</small>` : ""}
          </td>
          <td>
            <div class="table-actions">
              <button class="button button-secondary button-sm" type="button" data-view-seller-bids="${item.id}">
                ประวัติบิด (${item.bidCount})
              </button>
              ${item.canEditOrCancel ? `<button class="button button-secondary button-sm" type="button" data-edit-listing="${item.id}">แก้ไข</button><button class="button button-danger button-sm" type="button" data-delete-listing="${item.id}" data-name="${escapeHtml(item.name)}">ยกเลิก</button>` : ""}
            </div>
          </td>
        </tr>`).join("")
      : `<tr class="empty-row"><td colspan="5">ยังไม่มีสินค้าที่ลงขาย</td></tr>`;

    // Seller Finance Stats & Sold Orders
    const s = data.summary;
    document.getElementById("sellerFinanceStats").innerHTML = `
      <div><small>ยอดขายที่ชำระแล้ว</small><strong>${money(s.totalGross)}</strong><span>ก่อนหักค่าธรรมเนียม</span></div>
      <div><small>ค่าธรรมเนียมระบบ</small><strong>${money(s.totalFees)}</strong><span>อัตรา ${data.platformFeePercent}%</span></div>
      <div><small>ได้รับเงินโอนแล้ว</small><strong>${money(s.totalReleased)}</strong><span>ปล่อยเงินเรียบร้อย</span></div>
      <div><small>ยอดรอฝ่ายการเงินปล่อย</small><strong>${money(s.pendingPayout)}</strong><span>หลังยืนยันส่งมอบ/รับของ</span></div>
    `;

    document.getElementById("sellerOrdersRows").innerHTML = data.orders.length
      ? data.orders.map(o => `
        <tr>
          <td>
            <div class="table-product">
              <img class="table-thumb" src="${safeImageUrl(o.imageUrl)}" alt="" onerror="this.src='${imageFallback}'">
              <div><strong>${escapeHtml(o.itemName)}</strong><small>${escapeHtml(o.orderCode)}</small></div>
            </div>
          </td>
          <td>${escapeHtml(o.buyerName)}<small class="table-subline">${escapeHtml(o.buyerPhone || "")}</small></td>
          <td class="table-price">
            ${money(o.sellerNetAmount)}
            <small class="table-subline">ชำระ ${money(o.finalBuyerAmount)} - ค่าธรรมเนียม ${money(o.platformFeeAmount)}</small>
          </td>
          <td>
            ${paymentPill(o.paymentStatus, o.paymentMethod)}
            <small class="table-subline">${escapeHtml(paymentMethodLabel(o.paymentMethod))}</small>
            ${o.paymentNote ? `<small class="table-subline">โน้ต: ${escapeHtml(o.paymentNote)}</small>` : ""}
          </td>
          <td>
            ${meetupPill(o.meetupStatus)}
            ${o.meetupLocation ? `<small class="table-subline">⌖ ${escapeHtml(o.meetupLocation)} (${formatDateTime(o.meetupAt)})</small>` : ""}
          </td>
          <td>${payoutPill(o.payoutStatus)}${o.financeNote ? `<small class="table-subline">${escapeHtml(o.financeNote)}</small>` : ""}</td>
          <td>
            <div class="table-actions">
              ${o.paymentSlipUrl
                ? `<button class="button button-secondary button-sm" type="button" data-view-slip="${escapeHtml(o.paymentSlipUrl)}" data-slip-title="สลิป ${escapeHtml(o.orderCode)}" data-slip-note="${escapeHtml(o.paymentNote || "")}">ดูสลิป</button>` : ""}
              ${o.canVerifyDirectPayment
                ? `<button class="button button-primary button-sm" type="button" data-seller-verify-pay="${o.id}">ยืนยันได้รับเงินแล้ว</button>
                   <button class="button button-danger button-sm" type="button" data-seller-reject-pay="${o.id}" data-code="${escapeHtml(o.orderCode)}">ปฏิเสธ</button>` : ""}
              ${o.paymentStatus === "Paid" && o.meetupStatus !== "Completed"
                ? `<button class="button button-secondary button-sm" type="button" data-schedule-meetup="${o.id}" data-loc="${escapeHtml(o.meetupLocation || "")}">กำหนดนัดรับ</button>` : ""}
              ${o.paymentStatus === "Paid" && (o.meetupStatus === "NotScheduled" || o.meetupStatus === "Scheduled")
                ? `<button class="button button-primary button-sm" type="button" data-confirm-deliver="${o.id}">ยืนยันส่งมอบแล้ว</button>` : ""}
              ${o.conversationId
                ? `<button class="button button-secondary button-sm" type="button" data-open-conv="${o.conversationId}">แชท</button>` : ""}
            </div>
          </td>
        </tr>`).join("")
      : `<tr class="empty-row"><td colspan="7">ยังไม่มีรายการขายที่ปิดประมูล</td></tr>`;
  }

  // ==================== CHAT (BUYER & SELLER) ====================
  async function loadConversations(selectConvId = null) {
    conversationsCache = await api("/api/chat/conversations");
    renderConversationList();

    const targetId = selectConvId || activeConversationId || conversationsCache[0]?.id || null;
    if (targetId && conversationsCache.some(c => c.id === targetId)) {
      await openConversation(targetId);
    } else {
      activeConversationId = null;
      document.getElementById("chatHeader").innerHTML = `<span class="chat-avatar">▤</span><span><strong>ยังไม่มีบทสนทนา</strong><small>เปิดแชทได้จากหน้ารายละเอียดสินค้าหรือรายการที่ชนะประมูล</small></span>`;
      document.getElementById("chatMessages").innerHTML = `<div class="empty-state">ยังไม่มีข้อความ</div>`;
      document.getElementById("chatInput").disabled = true;
      document.getElementById("chatSendBtn").disabled = true;
    }
  }

  function renderConversationList() {
    const q = (document.getElementById("convSearchInput")?.value || "").trim().toLowerCase();
    const list = conversationsCache.filter(c =>
      !q || `${c.peerName} ${c.itemName} ${c.lastMessage}`.toLowerCase().includes(q));

    document.getElementById("conversationItems").innerHTML = list.length
      ? list.map(c => `
        <button class="conversation${c.id === activeConversationId ? " active" : ""}${c.unreadCount > 0 ? " has-unread" : ""}" type="button" data-select-conv="${c.id}">
          <span class="chat-avatar">${escapeHtml((c.peerName || "?").charAt(0))}</span>
          <span class="conversation-copy">
            <strong>${escapeHtml(c.peerName)} · ${escapeHtml(c.itemName)}</strong>
            <small>${c.hasOpenDispute ? "⚑ มีข้อพิพาท · " : ""}${escapeHtml(c.lastMessage)}</small>
          </span>
          <span class="conv-meta">
            <time>${formatDateShort(c.lastSentAt)}</time>
            ${c.unreadCount > 0 ? `<span class="conv-unread-pill">${c.unreadCount}</span>` : ""}
          </span>
        </button>`).join("")
      : `<div class="empty-state" style="border:0;">ไม่พบบทสนทนา</div>`;
  }

  async function openConversation(convId, { preserveScroll = false } = {}) {
    const isSameConv = activeConversationId === convId;
    const msgBox = document.getElementById("chatMessages");
    const prevScrollTop = msgBox ? msgBox.scrollTop : 0;
    const wasNearBottom = !msgBox || (msgBox.scrollHeight - msgBox.scrollTop - msgBox.clientHeight < 90);

    activeConversationId = convId;
    const conv = await api(`/api/chat/conversations/${convId}/messages`);
    const summary = conversationsCache.find(c => c.id === convId);
    if (summary) summary.unreadCount = 0;
    renderConversationList();
    pollNotifications(true);

    document.getElementById("chatHeader").innerHTML = `
      <span class="chat-avatar">${escapeHtml((conv.peerName || "?").charAt(0))}</span>
      <span>
        <strong>${escapeHtml(conv.peerName)}</strong>
        <small>เกี่ยวกับ: ${escapeHtml(conv.itemName)} ${summary?.orderCode ? `· ${escapeHtml(summary.orderCode)}` : ""}</small>
      </span>
      <div class="table-actions">
        <button class="button button-secondary button-sm" type="button" data-detail="${conv.auctionItemId}">ดูสินค้า ↗</button>
        <button class="button button-danger button-sm" type="button" data-report-dispute="${conv.id}" data-order-id="${summary?.orderPaymentId || ""}">⚑ แจ้งข้อพิพาท</button>
      </div>
    `;

    const disputeBanner = conv.disputes.length
      ? `<div class="info-banner banner-error" style="margin-bottom:4px;flex-shrink:0;"><strong>⚑ เคสนี้มีการแจ้งข้อพิพาท (${conv.disputes.length} รายการ)</strong>สถานะล่าสุด: ${escapeHtml(conv.disputes[0].status)} — ${escapeHtml(conv.disputes[0].reason)}${conv.disputes[0].adminNote ? ` | แอดมิน: ${escapeHtml(conv.disputes[0].adminNote)}` : ""}</div>`
      : "";

    const msgsHtml = conv.messages.length
      ? conv.messages.map(m => `
        <div class="chat-bubble ${m.isMine ? "sent" : "received"}">
          <span class="sender-tag">${escapeHtml(m.senderName)}</span>
          <span class="bubble-body">${escapeHtml(m.body)}</span>
          <small>${formatDateTime(m.sentAt)}</small>
        </div>`).join("")
      : `<div class="empty-state">เริ่มพิมพ์ข้อความเพื่อสอบถามรายละเอียดหรือนัดรับสินค้า</div>`;

    msgBox.innerHTML = disputeBanner + msgsHtml;
    if (preserveScroll && isSameConv && !wasNearBottom) {
      msgBox.scrollTop = prevScrollTop;
    } else {
      msgBox.scrollTop = msgBox.scrollHeight;
    }

    document.getElementById("chatInput").disabled = false;
    document.getElementById("chatSendBtn").disabled = false;
  }

  // ==================== ADMIN DASHBOARD ====================
  async function loadAdminDashboard() {
    const data = await api("/api/admin/dashboard");
    const st = data.stats;

    document.getElementById("adminStatsCards").innerHTML = `
      <div><small>ผู้ขายรออนุมัติ</small><strong>${st.pendingSellersCount}</strong><span>ใบสมัครใหม่</span></div>
      <div><small>สินค้ารออนุมัติ</small><strong>${st.pendingAuctionsCount}</strong><span>รอตรวจสอบ</span></div>
      <div><small>กำลังเปิดประมูล</small><strong>${st.liveAuctionsCount}</strong><span>รายการที่แสดงอยู่</span></div>
      <div><small>ข้อพิพาทที่เปิดอยู่</small><strong>${st.openDisputesCount}</strong><span>เคสรอตรวจสอบ</span></div>
    `;

    // Pending Sellers
    document.getElementById("adminSellerRows").innerHTML = data.pendingSellers.length
      ? data.pendingSellers.map(u => `
        <tr>
          <td><strong>${escapeHtml(u.displayName)}</strong><small class="table-subline">ร้าน: ${escapeHtml(u.shopName || "-")} · ${escapeHtml(u.email)}</small></td>
          <td>${escapeHtml(u.phone || "-")}<small class="table-subline">${escapeHtml(u.bankAccount || "-")}</small></td>
          <td>${escapeHtml(u.sellerBio || "-")}</td>
          <td>
            <div class="table-actions">
              <button class="button button-primary button-sm" type="button" data-approve-seller="${u.id}">อนุมัติ</button>
              <button class="button button-danger button-sm" type="button" data-reject-seller="${u.id}" data-name="${escapeHtml(u.displayName)}">ปฏิเสธ</button>
            </div>
          </td>
        </tr>`).join("")
      : `<tr class="empty-row"><td colspan="4">ไม่มีใบสมัครผู้ขายรออนุมัติ</td></tr>`;

    // Pending Auctions
    document.getElementById("adminAuctionRows").innerHTML = data.pendingAuctions.length
      ? data.pendingAuctions.map(a => `
        <tr>
          <td>
            <div class="table-product">
              <img class="table-thumb" src="${safeImageUrl(a.imageUrl)}" alt="" onerror="this.src='${imageFallback}'">
              <div><strong>${escapeHtml(a.name)}</strong><small>${escapeHtml(a.categoryName)} · ${escapeHtml(a.condition)}</small></div>
            </div>
          </td>
          <td>${escapeHtml(a.sellerName)}<small class="table-subline">${escapeHtml(a.sellerEmail)}</small></td>
          <td class="table-price">${money(a.startPrice)}<small class="table-subline">ขั้นต่ำ +${money(a.minIncrement)}</small></td>
          <td>${formatDateTime(a.startAt)}<small class="table-subline">ถึง ${formatDateTime(a.endAt)}</small></td>
          <td>
            <div class="table-actions">
              <button class="button button-secondary button-sm" type="button" data-detail="${a.id}">ดูรายละเอียด</button>
              <button class="button button-primary button-sm" type="button" data-approve-auction="${a.id}">อนุมัติ</button>
              <button class="button button-danger button-sm" type="button" data-reject-auction="${a.id}" data-name="${escapeHtml(a.name)}">ปฏิเสธ</button>
            </div>
          </td>
        </tr>`).join("")
      : `<tr class="empty-row"><td colspan="5">ไม่มีสินค้ารอตรวจสอบ</td></tr>`;

    // Users
    document.getElementById("adminUsersRows").innerHTML = data.users.map(u => `
      <tr>
        <td><strong>${escapeHtml(u.displayName)}</strong><small class="table-subline">${escapeHtml(u.email)}</small></td>
        <td><span class="role-badge role-${escapeHtml(u.role)}">${escapeHtml(roleLabels[u.role] || u.role)}</span></td>
        <td>${u.sellerStatus === "None" ? "-" : statusPill(u.sellerStatus === "Approved" ? "green" : u.sellerStatus === "Pending" ? "pending" : "red", u.sellerStatus)}</td>
        <td>
          ${u.isSuspended
            ? `${statusPill("red", "ถูกระงับ")}<small class="table-subline">${escapeHtml(u.suspendReason || "")}</small>`
            : statusPill("green", "ใช้งานปกติ")}
        </td>
        <td>
          ${u.id === currentUser?.id
            ? `<small style="color:var(--muted);">บัญชีปัจจุบัน</small>`
            : u.isSuspended
              ? `<button class="button button-primary button-sm" type="button" data-unsuspend-user="${u.id}">ปลดระงับ</button>`
              : `<button class="button button-danger button-sm" type="button" data-suspend-user="${u.id}" data-name="${escapeHtml(u.displayName)}">ระงับบัญชี</button>`}
        </td>
      </tr>`).join("");

    // Categories
    document.getElementById("adminCategoriesRows").innerHTML = data.categories.map(c => `
      <tr>
        <td><strong>${escapeHtml(c.symbol)} ${escapeHtml(c.name)}</strong></td>
        <td>${escapeHtml(c.description || "-")}</td>
        <td>${c.itemCount} รายการ</td>
        <td>${c.isActive ? statusPill("green", "เปิดใช้งาน") : statusPill("muted", "ปิดใช้งาน")}</td>
        <td>
          <div class="table-actions">
            <button class="button button-secondary button-sm" type="button"
              data-edit-cat="${c.id}" data-cat-name="${escapeHtml(c.name)}" data-cat-sym="${escapeHtml(c.symbol)}" data-cat-desc="${escapeHtml(c.description || "")}" data-cat-active="${c.isActive}">แก้ไข</button>
            <button class="button button-danger button-sm" type="button" data-delete-cat="${c.id}">${c.itemCount > 0 ? "ปิดใช้งาน" : "ลบ"}</button>
          </div>
        </td>
      </tr>`).join("");

    // Disputes
    document.getElementById("adminDisputesRows").innerHTML = data.disputes.length
      ? data.disputes.map(d => `
        <tr>
          <td><strong>${escapeHtml(d.itemName)}</strong><small class="table-subline">${escapeHtml(d.orderCode || "ก่อนปิดประมูล")} · เคส #${d.id}</small></td>
          <td>ผู้ซื้อ: ${escapeHtml(d.buyerName)}<small class="table-subline">ผู้ขาย: ${escapeHtml(d.sellerName)}</small></td>
          <td><strong>${escapeHtml(d.reason)}</strong><small class="table-subline">แจ้งโดย ${escapeHtml(d.reporterName)} · ${formatDateTime(d.createdAt)}</small></td>
          <td>${statusPill(d.status === "Resolved" ? "green" : d.status === "Investigating" ? "blue" : "red", d.status)}</td>
          <td><button class="button button-primary button-sm" type="button" data-inspect-dispute="${d.id}">ตรวจสอบแชท & จัดการเคส</button></td>
        </tr>`).join("")
      : `<tr class="empty-row"><td colspan="5">ยังไม่มีเรื่องร้องเรียนข้อพิพาท</td></tr>`;

    // Read-only Winners Archive
    document.getElementById("adminWinnersRows").innerHTML = data.winnersReadOnly.length
      ? data.winnersReadOnly.map(w => `
        <tr>
          <td>
            <div class="table-product">
              <img class="table-thumb" src="${safeImageUrl(w.imageUrl)}" alt="" onerror="this.src='${imageFallback}'">
              <div><strong>${escapeHtml(w.itemName)}</strong><small>${escapeHtml(w.orderCode)} · ${escapeHtml(w.categoryName)}</small></div>
            </div>
          </td>
          <td>${escapeHtml(w.winnerName)}</td>
          <td>${escapeHtml(w.sellerName)}</td>
          <td class="table-price">${money(w.winningPrice)}<small class="table-subline">สุทธิ ${money(w.finalBuyerAmount)}</small></td>
          <td>${paymentPill(w.paymentStatus)}</td>
          <td>${meetupPill(w.meetupStatus)}</td>
          <td>${formatDateShort(w.createdAt)}</td>
        </tr>`).join("")
      : `<tr class="empty-row"><td colspan="7">ยังไม่มีประวัติผู้ชนะประมูล</td></tr>`;
  }

  // ==================== FINANCE DASHBOARD ====================
  async function loadFinanceDashboard() {
    const data = await api("/api/finance/dashboard");
    const s = data.summary;
    const set = data.settings;

    document.getElementById("financeSummaryCards").innerHTML = `
      <div><small>ยอดผู้ซื้อชำระรวม</small><strong>${money(s.totalBuyerPaid)}</strong><span>ชำระแล้ว ${s.paidOrdersCount} · รอตรวจสลิป ${s.pendingVerifyCount}</span></div>
      <div><small>รายได้ค่าธรรมเนียม (Escrow)</small><strong>${money(s.totalPlatformFee)}</strong><span>หัก ${set.platformFeePercent}% จากยอดระบบกลาง</span></div>
      <div><small>รอปล่อยเงินให้ผู้ขาย</small><strong>${money(s.pendingRelease)}</strong><span>ปล่อยแล้ว ${money(s.totalReleased)}</span></div>
      <div><small>ยอดคืนเงินทั้งหมด</small><strong>${money(s.totalRefunded)}</strong><span>รอชำระ ${s.unpaidOrdersCount} รายการ</span></div>
    `;

    document.getElementById("financeOrdersRows").innerHTML = data.orders.length
      ? data.orders.map(o => `
        <tr>
          <td>
            <div class="table-product">
              <img class="table-thumb" src="${safeImageUrl(o.imageUrl)}" alt="" onerror="this.src='${imageFallback}'">
              <div>
                <strong>${escapeHtml(o.itemName)}</strong>
                <small>${escapeHtml(o.orderCode)} · ${escapeHtml(paymentMethodLabel(o.paymentMethod))} ${o.hasOpenDispute ? "· ⚑ มีข้อพิพาท" : ""}</small>
              </div>
            </div>
          </td>
          <td>ผู้ซื้อ: ${escapeHtml(o.buyerName)}<small class="table-subline">ผู้ขาย: ${escapeHtml(o.sellerName)} (${escapeHtml(o.sellerBank)})</small></td>
          <td>
            ${money(o.winningPrice)}
            ${o.discountAmount > 0 ? `<small class="table-subline" style="color:#966222;">ลด ${o.discountPercent}% (-${money(o.discountAmount)})</small>` : `<small class="table-subline">ไม่มีส่วนลด</small>`}
          </td>
          <td class="table-price">
            รับ ${money(o.finalBuyerAmount)}
            <small class="table-subline">${o.paymentMethod === "DirectToSeller" ? "จ่ายตรงผู้ขาย (ไม่หักค่าธรรมเนียมกลาง)" : `ค่าธรรมเนียม ${money(o.platformFeeAmount)} · โอนผู้ขาย <strong>${money(o.sellerNetAmount)}</strong>`}</small>
          </td>
          <td>
            ${paymentPill(o.paymentStatus, o.paymentMethod)}
            ${o.paymentNote ? `<small class="table-subline">โน้ต: ${escapeHtml(o.paymentNote)}</small>` : ""}
            <div style="margin-top:4px;">${meetupPill(o.meetupStatus)}</div>
          </td>
          <td>${payoutPill(o.payoutStatus)}${o.financeNote ? `<small class="table-subline">${escapeHtml(o.financeNote)}</small>` : ""}</td>
          <td>
            <div class="table-actions">
              ${o.paymentSlipUrl ? `<button class="button button-secondary button-sm" type="button" data-view-slip="${escapeHtml(o.paymentSlipUrl)}" data-slip-title="สลิป ${escapeHtml(o.orderCode)} (${escapeHtml(o.buyerName)})" data-slip-note="${escapeHtml(o.paymentNote || "")}">ดูสลิป</button>` : ""}
              ${o.canVerifyEscrowPayment ? `<button class="button button-primary button-sm" type="button" data-finance-verify-pay="${o.id}">ยืนยันสลิป</button><button class="button button-danger button-sm" type="button" data-finance-reject-pay="${o.id}" data-code="${escapeHtml(o.orderCode)}">ปฏิเสธสลิป</button>` : ""}
              ${o.canRelease ? `<button class="button button-primary button-sm" type="button" data-finance-release="${o.id}" data-code="${escapeHtml(o.orderCode)}" data-amt="${o.sellerNetAmount}">ปล่อยเงินผู้ขาย</button>` : ""}
              ${o.canRefund ? `<button class="button button-danger button-sm" type="button" data-finance-refund="${o.id}" data-code="${escapeHtml(o.orderCode)}" data-amt="${o.finalBuyerAmount}">คืนเงินผู้ซื้อ</button>` : ""}
              ${!o.canVerifyEscrowPayment && !o.canRelease && !o.canRefund && o.payoutStatus === "Pending"
                ? `<small style="color:var(--muted);">${o.paymentStatus === "Unpaid" ? "รอผู้ซื้อชำระเงิน" : o.paymentStatus === "PendingVerification" ? "รอผู้ขายยืนยันจ่ายตรง" : "รอยืนยันส่งมอบ/รับของ"}</small>` : ""}
              ${o.releaseBlockedReason ? `<small class="table-subline" style="color:var(--rose);font-weight:600;">${escapeHtml(o.releaseBlockedReason)}</small>` : ""}
            </div>
          </td>
        </tr>`).join("")
      : `<tr class="empty-row"><td colspan="7">ยังไม่มีรายการชำระเงิน</td></tr>`;

    // Rules Form
    const rf = document.getElementById("financeRulesForm");
    rf.sameCategoryDiscountPercent.value = set.sameCategoryDiscountPercent;
    rf.platformFeePercent.value = set.platformFeePercent;
    rf.globalMinBidIncrement.value = set.globalMinBidIncrement;
    if (rf.platformPromptPayId) rf.platformPromptPayId.value = set.platformPromptPayId || "";
    if (rf.platformAccountName) rf.platformAccountName.value = set.platformAccountName || "";
    document.getElementById("financeRulesUpdated").textContent = `อัปเดตล่าสุด: ${formatDateTime(set.updatedAt)}`;

    // Report Cards & Table
    document.getElementById("financeReportCards").innerHTML = `
      <div><small>มูลค่าปิดประมูลรวม (GMV)</small><strong>${money(s.grossSales)}</strong><span>รายการที่ชำระแล้ว</span></div>
      <div><small>ส่วนลดหมวดเดียวกันรวม</small><strong>${money(s.totalDiscounts)}</strong><span>ส่งเสริมการซื้อซ้ำ</span></div>
      <div><small>ค่าธรรมเนียมสุทธิระบบ</small><strong>${money(s.totalPlatformFee)}</strong><span>รายได้แพลตฟอร์ม</span></div>
      <div><small>ปล่อยเงินให้ผู้ขายแล้ว</small><strong>${money(s.totalReleased)}</strong><span>คืนเงิน ${money(s.totalRefunded)}</span></div>
    `;

    document.getElementById("financeReportRows").innerHTML = data.orders.length
      ? data.orders.map(o => `
        <tr>
          <td><strong>${escapeHtml(o.orderCode)}</strong></td>
          <td>${escapeHtml(o.itemName)}<small class="table-subline">${escapeHtml(o.categoryName)}</small></td>
          <td>${escapeHtml(o.buyerName)}</td>
          <td>${escapeHtml(o.sellerName)}</td>
          <td>${money(o.winningPrice)}</td>
          <td>${o.discountAmount > 0 ? `-${money(o.discountAmount)} (${o.discountPercent}%)` : "-"}</td>
          <td class="table-price">${money(o.finalBuyerAmount)}</td>
          <td>${money(o.platformFeeAmount)}</td>
          <td class="table-price">${money(o.sellerNetAmount)}</td>
          <td>${paymentPill(o.paymentStatus, o.paymentMethod)} ${payoutPill(o.payoutStatus)}</td>
        </tr>`).join("")
      : `<tr class="empty-row"><td colspan="10">ยังไม่มีข้อมูลรายงาน</td></tr>`;
  }

  // ==================== ACTION MODALS ====================
  function showReasonModal({ title, subtitle, label, placeholder, submitText, danger = true, onSubmit }) {
    document.getElementById("actionDialogContent").innerHTML = `
      <h2 class="auth-title">${escapeHtml(title)}</h2>
      <p class="auth-subtitle">${escapeHtml(subtitle)}</p>
      <form id="dynamicActionForm">
        <label class="field-label">${escapeHtml(label)} <span>*</span>
          <textarea name="reason" rows="3" required placeholder="${escapeHtml(placeholder)}"></textarea>
        </label>
        <div class="table-actions" style="justify-content:flex-end;">
          <button type="button" class="button button-secondary" data-close-action="1">ยกเลิก</button>
          <button type="submit" class="button ${danger ? "button-danger" : "button-primary"}">${escapeHtml(submitText)}</button>
        </div>
      </form>
    `;
    document.getElementById("dynamicActionForm").addEventListener("submit", async e => {
      e.preventDefault();
      const reason = new FormData(e.currentTarget).get("reason");
      try {
        await onSubmit(String(reason || "").trim());
        actionDialog.close();
      } catch (err) {
        showToast(err.message, true);
      }
    });
    openDialogSafe(actionDialog);
  }

  async function openDisputeInspector(disputeId) {
    try {
      const d = await api(`/api/admin/disputes/${disputeId}/messages`);
      document.getElementById("actionDialogContent").innerHTML = `
        <h2 class="auth-title">ตรวจสอบข้อพิพาท #${d.id}</h2>
        <p class="auth-subtitle">สินค้า: <strong>${escapeHtml(d.itemName)}</strong> ${d.orderCode ? `(${escapeHtml(d.orderCode)})` : ""} · ผู้ซื้อ: ${escapeHtml(d.buyerName)} · ผู้ขาย: ${escapeHtml(d.sellerName)}</p>
        <div class="info-banner banner-error">
          <strong>หัวข้อ: ${escapeHtml(d.reason)} (แจ้งโดย ${escapeHtml(d.reporterName)})</strong>
          ${escapeHtml(d.details)}
        </div>
        <h3 style="font-size:13px;margin-bottom:6px;">บันทึกแชทเฉพาะเคสที่ถูกร้องเรียน (${d.messages.length} ข้อความ)</h3>
        <div class="dispute-chat-box">
          ${d.messages.length ? d.messages.map(m => `
            <div class="chat-bubble ${m.senderRole === "ผู้ขาย" ? "sent" : "received"}" style="max-width:92%;">
              <span class="sender-tag">${escapeHtml(m.senderName)} (${escapeHtml(m.senderRole)})</span>
              ${escapeHtml(m.body)}
              <small>${formatDateTime(m.sentAt)}</small>
            </div>`).join("") : `<div class="empty-state">ยังไม่มีข้อความในห้องแชทนี้</div>`}
        </div>
        <form id="adminDisputeUpdateForm">
          <div class="field-row">
            <label class="field-label">สถานะการพิจารณา
              <select name="status">
                <option value="Open" ${d.status === "Open" ? "selected" : ""}>Open (เปิดเรื่อง)</option>
                <option value="Investigating" ${d.status === "Investigating" ? "selected" : ""}>Investigating (กำลังตรวจสอบ)</option>
                <option value="Resolved" ${d.status === "Resolved" ? "selected" : ""}>Resolved (แก้ไขแล้ว)</option>
              </select>
            </label>
          </div>
          <label class="field-label">บันทึกผลการตรวจสอบของแอดมิน
            <textarea name="adminNote" rows="2" placeholder="ระบุข้อสรุปหรือคำแนะนำส่งต่อฝ่ายการเงิน">${escapeHtml(d.adminNote || "")}</textarea>
          </label>
          <div class="table-actions" style="justify-content:flex-end;">
            <button type="button" class="button button-secondary" data-close-action="1">ปิด</button>
            <button type="submit" class="button button-primary">บันทึกผลข้อพิพาท</button>
          </div>
        </form>
      `;
      document.getElementById("adminDisputeUpdateForm").addEventListener("submit", async e => {
        e.preventDefault();
        const fd = new FormData(e.currentTarget);
        try {
          const res = await api(`/api/admin/disputes/${disputeId}/update`, {
            method: "POST",
            body: JSON.stringify({ status: fd.get("status"), adminNote: fd.get("adminNote") })
          });
          showToast(res.message);
          actionDialog.close();
          await loadAdminDashboard();
        } catch (err) { showToast(err.message, true); }
      });
      openDialogSafe(actionDialog);
    } catch (err) {
      showToast(err.message, true);
    }
  }

  // ==================== GLOBAL EVENT LISTENERS ====================
  document.getElementById("mobileMenuBtn").addEventListener("click", () => {
    document.getElementById("sidebar").classList.toggle("mobile-open");
    document.getElementById("sidebarBackdrop").classList.toggle("hidden");
  });
  document.getElementById("sidebarBackdrop").addEventListener("click", closeMobileDrawer);
  document.getElementById("heroAuthBtn")?.addEventListener("click", () => openAuthModal("login"));
  document.getElementById("printReportBtn")?.addEventListener("click", () => window.print());

  document.getElementById("viewToggleBtn")?.addEventListener("click", () => {
    listViewMode = !listViewMode;
    document.getElementById("productGrid").classList.toggle("list-view", listViewMode);
  });

  document.querySelectorAll(".sort-button[data-sort]").forEach(btn => {
    btn.addEventListener("click", () => {
      sortMode = btn.dataset.sort;
      document.querySelectorAll(".sort-button[data-sort]").forEach(b => b.classList.toggle("active", b === btn));
      loadAuctions().catch(e => showToast(e.message, true));
    });
  });

  let searchTimer = null;
  document.getElementById("searchInput").addEventListener("input", e => {
    searchTerm = e.target.value.trim();
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => loadAuctions().catch(err => showToast(err.message, true)), 220);
  });

  document.getElementById("convSearchInput")?.addEventListener("input", renderConversationList);

  // Auth Tabs & Register role toggle
  document.querySelectorAll(".auth-tab").forEach(tab => {
    tab.addEventListener("click", () => openAuthModal(tab.dataset.authTab));
  });
  document.getElementById("regRoleSelect")?.addEventListener("change", e => {
    const isSeller = e.target.value === "Seller";
    document.getElementById("regSellerFields").classList.toggle("hidden", !isSeller);
    document.querySelectorAll("#regSellerFields input[name='shopName'], #regSellerFields input[name='phone'], #regSellerFields input[name='bankAccount']")
      .forEach(inp => { inp.required = isSeller; });
  });

  // Login Form
  document.getElementById("loginForm").addEventListener("submit", async e => {
    e.preventDefault();
    const form = e.currentTarget;
    const fd = new FormData(form);
    try {
      const res = await api("/api/auth/login", {
        method: "POST",
        body: JSON.stringify({ email: fd.get("email"), password: fd.get("password") })
      });
      authDialog.close();
      form.reset();
      showToast(res.message);
      await refreshSession(true);
    } catch (err) { showToast(err.message, true); }
  });

  // Register Form
  document.getElementById("registerForm").addEventListener("submit", async e => {
    e.preventDefault();
    const form = e.currentTarget;
    const fd = new FormData(form);
    try {
      const res = await api("/api/auth/register", {
        method: "POST",
        body: JSON.stringify({
          displayName: fd.get("displayName"),
          email: fd.get("email"),
          password: fd.get("password"),
          role: fd.get("role"),
          shopName: fd.get("shopName"),
          phone: fd.get("phone"),
          bankAccount: fd.get("bankAccount"),
          sellerBio: fd.get("sellerBio")
        })
      });
      authDialog.close();
      form.reset();
      document.getElementById("regSellerFields").classList.add("hidden");
      document.querySelectorAll("#regSellerFields input[name='shopName'], #regSellerFields input[name='phone'], #regSellerFields input[name='bankAccount']")
        .forEach(inp => { inp.required = false; });
      showToast(res.message);
      await refreshSession(true);
    } catch (err) { showToast(err.message, true); }
  });

  // Notification Bell Toggle & Mark All Read
  document.getElementById("notifBellBtn")?.addEventListener("click", async e => {
    e.stopPropagation();
    const dropdown = document.getElementById("notifDropdown");
    const isOpening = dropdown.classList.contains("hidden");
    dropdown.classList.toggle("hidden");
    if (isOpening) await pollNotifications(true);
  });

  document.getElementById("markAllReadBtn")?.addEventListener("click", async e => {
    e.stopPropagation();
    try {
      await api("/api/notifications/read-all", { method: "POST" });
      conversationsCache.forEach(c => { c.unreadCount = 0; });
      if (activeView === "messages") renderConversationList();
      await pollNotifications(true);
      showToast("ทำเครื่องหมายว่าอ่านทั้งหมดแล้ว");
    } catch (err) { showToast(err.message, true); }
  });

  // Bid Form inside auctionDialog
  auctionDialog.addEventListener("submit", async e => {
    if (e.target.id !== "bidForm") return;
    e.preventDefault();
    const form = e.target;
    if (form.dataset.endAt && parseUtcDate(form.dataset.endAt).getTime() <= Date.now()) {
      showToast("รายการนี้ปิดประมูลแล้ว ไม่สามารถเสนอราคาได้", true);
      await openProduct(selectedProductId);
      if (activeView === "today") await loadAuctions();
      return;
    }
    const amount = Number(new FormData(form).get("bid"));
    try {
      const res = await api(`/api/auctions/${selectedProductId}/bid`, {
        method: "POST",
        body: JSON.stringify({ amount })
      });
      showToast(res.message);
      await openProduct(selectedProductId);
      if (activeView === "today") await loadAuctions();
      else if (activeView === "mybids") await loadBuyerDashboard();
    } catch (err) {
      showToast(err.message, true);
      await openProduct(selectedProductId);
      if (activeView === "today") await loadAuctions();
    }
  });

  // Seller: Upload product image file
  document.getElementById("listingImageFile")?.addEventListener("change", async e => {
    const file = e.currentTarget.files?.[0];
    if (!file) return;
    const fd = new FormData();
    fd.append("file", file);
    try {
      const res = await api("/api/seller/upload-image", { method: "POST", body: fd });
      document.getElementById("listingImageUrlInput").value = res.imageUrl;
      document.getElementById("listingImagePreview").src = res.imageUrl;
      document.getElementById("listingImagePreviewWrap").classList.remove("hidden");
      showToast(res.message);
    } catch (err) {
      showToast(err.message, true);
    }
  });

  // Seller: Create Listing Form
  document.getElementById("listingForm")?.addEventListener("submit", async e => {
    e.preventDefault();
    const form = e.currentTarget;
    const fd = new FormData(form);
    try {
      const res = await api("/api/seller/listings", {
        method: "POST",
        body: JSON.stringify({
          name: fd.get("name"),
          categoryId: Number(fd.get("categoryId")),
          condition: fd.get("condition"),
          description: fd.get("description"),
          imageUrl: fd.get("imageUrl"),
          startPrice: Number(fd.get("startPrice")),
          minIncrement: Number(fd.get("minIncrement")),
          startAt: new Date(String(fd.get("startAt"))).toISOString(),
          endAt: new Date(String(fd.get("endAt"))).toISOString()
        })
      });
      showToast(res.message);
      form.reset();
      document.getElementById("listingImagePreviewWrap")?.classList.add("hidden");
      initListingDates();
      await loadSellerDashboard();
    } catch (err) { showToast(err.message, true); }
  });

  // Forgot & Reset Password Forms
  document.getElementById("forgotPasswordForm")?.addEventListener("submit", async e => {
    e.preventDefault();
    const form = e.currentTarget;
    const email = String(new FormData(form).get("email") || "").trim();
    try {
      const res = await api("/api/auth/forgot-password", {
        method: "POST",
        body: JSON.stringify({ email })
      });
      showToast(res.message);
      const resetForm = document.getElementById("resetPasswordForm");
      if (resetForm) resetForm.email.value = email;
      const devNotice = document.getElementById("forgotDevNotice");
      if (devNotice && res.devResetCode) {
        devNotice.innerHTML = `<strong>โหมดพัฒนา (Development)</strong>รหัสรีเซ็ต 6 หลักของคุณคือ: <strong>${escapeHtml(res.devResetCode)}</strong>`;
        devNotice.classList.remove("hidden");
        if (resetForm) resetForm.resetToken.value = res.devResetCode;
      }
    } catch (err) { showToast(err.message, true); }
  });

  document.getElementById("resetPasswordForm")?.addEventListener("submit", async e => {
    e.preventDefault();
    const form = e.currentTarget;
    const fd = new FormData(form);
    try {
      const res = await api("/api/auth/reset-password", {
        method: "POST",
        body: JSON.stringify({
          email: fd.get("email"),
          resetToken: fd.get("resetToken"),
          newPassword: fd.get("newPassword")
        })
      });
      showToast(res.message);
      form.reset();
      document.getElementById("forgotDevNotice")?.classList.add("hidden");
      await openAuthModal("login");
    } catch (err) { showToast(err.message, true); }
  });

  // Seller: Reapply Form
  document.getElementById("sellerReapplyForm")?.addEventListener("submit", async e => {
    e.preventDefault();
    const fd = new FormData(e.currentTarget);
    try {
      const res = await api("/api/seller/apply", {
        method: "POST",
        body: JSON.stringify({
          shopName: fd.get("shopName"),
          phone: fd.get("phone"),
          bankAccount: fd.get("bankAccount"),
          sellerBio: fd.get("sellerBio")
        })
      });
      showToast(res.message);
      await loadSellerDashboard();
    } catch (err) { showToast(err.message, true); }
  });

  // Buyer: Apply to become Seller (consolidated endpoint)
  document.getElementById("buyerApplySellerForm")?.addEventListener("submit", async e => {
    e.preventDefault();
    const fd = new FormData(e.currentTarget);
    try {
      const res = await api("/api/seller/apply", {
        method: "POST",
        body: JSON.stringify({
          shopName: fd.get("shopName"),
          phone: fd.get("phone"),
          bankAccount: fd.get("bankAccount"),
          sellerBio: fd.get("sellerBio")
        })
      });
      currentUser = res.user;
      showToast(res.message);
      renderBuyerApplySeller();
    } catch (err) { showToast(err.message, true); }
  });

  // Chat Send Form
  document.getElementById("chatForm")?.addEventListener("submit", async e => {
    e.preventDefault();
    if (!activeConversationId) return;
    const input = document.getElementById("chatInput");
    const body = input.value.trim();
    if (!body) return;
    try {
      await api(`/api/chat/conversations/${activeConversationId}/messages`, {
        method: "POST",
        body: JSON.stringify({ body })
      });
      input.value = "";
      await loadConversations(activeConversationId);
      input.focus();
    } catch (err) { showToast(err.message, true); }
  });

  // Admin: Category Add/Edit Form
  document.getElementById("adminCategoryForm")?.addEventListener("submit", async e => {
    e.preventDefault();
    const form = e.currentTarget;
    const fd = new FormData(form);
    const id = fd.get("id");
    const payload = {
      name: fd.get("name"),
      symbol: fd.get("symbol"),
      description: fd.get("description"),
      isActive: true
    };
    try {
      const res = id
        ? await api(`/api/admin/categories/${id}`, { method: "PUT", body: JSON.stringify(payload) })
        : await api("/api/admin/categories", { method: "POST", body: JSON.stringify(payload) });
      showToast(res.message);
      form.reset();
      form.id.value = "";
      form.symbol.value = "✦";
      document.getElementById("catSubmitBtn").textContent = "เพิ่มหมวดหมู่";
      document.getElementById("catCancelEditBtn").classList.add("hidden");
      await loadAdminDashboard();
    } catch (err) { showToast(err.message, true); }
  });

  document.getElementById("catCancelEditBtn")?.addEventListener("click", () => {
    const form = document.getElementById("adminCategoryForm");
    form.reset();
    form.id.value = "";
    form.symbol.value = "✦";
    document.getElementById("catSubmitBtn").textContent = "เพิ่มหมวดหมู่";
    document.getElementById("catCancelEditBtn").classList.add("hidden");
  });

  // Finance: Update Settings Form
  document.getElementById("financeRulesForm")?.addEventListener("submit", async e => {
    e.preventDefault();
    const fd = new FormData(e.currentTarget);
    try {
      const res = await api("/api/finance/settings", {
        method: "PUT",
        body: JSON.stringify({
          sameCategoryDiscountPercent: Number(fd.get("sameCategoryDiscountPercent")),
          platformFeePercent: Number(fd.get("platformFeePercent")),
          globalMinBidIncrement: Number(fd.get("globalMinBidIncrement")),
          platformPromptPayId: fd.get("platformPromptPayId"),
          platformAccountName: fd.get("platformAccountName")
        })
      });
      showToast(res.message);
      await loadFinanceDashboard();
    } catch (err) { showToast(err.message, true); }
  });

  // Delegated Clicks
  document.addEventListener("click", async event => {
    const target = event.target instanceof Element ? event.target : null;
    if (!target) return;

    if (!target.closest("#notifWrapper")) {
      document.getElementById("notifDropdown")?.classList.add("hidden");
    }

    const notifItem = target.closest("[data-notif-view]");
    if (notifItem) {
      const notifId = notifItem.dataset.notifId;
      const targetView = notifItem.dataset.notifView;
      const refId = notifItem.dataset.notifRef ? Number(notifItem.dataset.notifRef) : null;
      document.getElementById("notifDropdown")?.classList.add("hidden");
      if (notifId) {
        api(`/api/notifications/${notifId}/read`, { method: "POST" }).catch(() => {});
      }
      if (targetView === "messages" && refId) {
        activeConversationId = refId;
      }
      if (targetView) {
        await switchView(targetView);
        if (targetView === "today" && refId) {
          await openProduct(refId);
        }
      }
      await pollNotifications(true);
      return;
    }

    // Auth tab buttons (including Forgot Password links)
    const authTabBtn = target.closest("[data-auth-tab]");
    if (authTabBtn) {
      await openAuthModal(authTabBtn.dataset.authTab);
      return;
    }

    // Audit Log filter buttons
    const auditFilterBtn = target.closest("[data-audit-filter]");
    if (auditFilterBtn) {
      document.querySelectorAll("[data-audit-filter]").forEach(b => b.classList.toggle("active", b === auditFilterBtn));
      await loadAuditLogs(auditFilterBtn.dataset.auditFilter).catch(err => showToast(err.message, true));
      return;
    }

    // Change Password Modal
    if (target.closest("[data-open-change-pw]")) {
      document.getElementById("actionDialogContent").innerHTML = `
        <h2 class="auth-title">เปลี่ยนรหัสผ่าน</h2>
        <p class="auth-subtitle">กรอกรหัสผ่านเดิมและตั้งรหัสผ่านใหม่อย่างน้อย 6 ตัวอักษร</p>
        <form id="changePasswordForm">
          <label class="field-label">รหัสผ่านเดิม <span>*</span><input name="currentPassword" type="password" required></label>
          <label class="field-label">รหัสผ่านใหม่ <span>*</span><input name="newPassword" type="password" minlength="6" required></label>
          <div class="table-actions" style="justify-content:flex-end;">
            <button type="button" class="button button-secondary" data-close-action="1">ยกเลิก</button>
            <button type="submit" class="button button-primary">บันทึกรหัสผ่านใหม่</button>
          </div>
        </form>
      `;
      document.getElementById("changePasswordForm").addEventListener("submit", async e => {
        e.preventDefault();
        const fd = new FormData(e.currentTarget);
        try {
          const res = await api("/api/auth/change-password", {
            method: "POST",
            body: JSON.stringify({ currentPassword: fd.get("currentPassword"), newPassword: fd.get("newPassword") })
          });
          showToast(res.message);
          actionDialog.close();
        } catch (err) { showToast(err.message, true); }
      });
      openDialogSafe(actionDialog);
      return;
    }

    // Seller: Edit Pending/Rejected Listing
    const editListBtn = target.closest("[data-edit-listing]");
    if (editListBtn) {
      const item = sellerListingsCache.find(x => x.id === Number(editListBtn.dataset.editListing));
      if (!item) return;
      const toLocal = iso => {
        const d = parseUtcDate(iso);
        return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
      };
      const catOpts = cachedCategories.map(c => `<option value="${c.id}" ${c.id === item.categoryId ? "selected" : ""}>${escapeHtml(c.name)}</option>`).join("");
      document.getElementById("actionDialogContent").innerHTML = `
        <h2 class="auth-title">แก้ไขสินค้า: ${escapeHtml(item.name)}</h2>
        <p class="auth-subtitle">หลังบันทึก ระบบจะส่งให้แอดมินตรวจสอบอีกครั้ง</p>
        <form id="editListingForm">
          <label class="field-label">ชื่อสินค้า <span>*</span><input name="name" required maxlength="100" value="${escapeHtml(item.name)}"></label>
          <div class="field-row">
            <label class="field-label">หมวดหมู่ <span>*</span><select name="categoryId" required>${catOpts}</select></label>
            <label class="field-label">สภาพสินค้า <span>*</span><input name="condition" required value="${escapeHtml(item.condition)}"></label>
          </div>
          <label class="field-label">รายละเอียด <span>*</span><textarea name="description" rows="3" required>${escapeHtml(item.description)}</textarea></label>
          <div class="field-row">
            <label class="field-label">อัปโหลดรูปใหม่ (ถ้าต้องการ)<input type="file" id="editListingImgFile" accept=".jpg,.jpeg,.png,.webp,image/*"></label>
            <label class="field-label">ลิงก์รูปภาพ<input name="imageUrl" id="editListingImgUrl" value="${escapeHtml(item.imageUrl || "")}"></label>
          </div>
          <div class="field-row">
            <label class="field-label">ราคาเริ่มต้น (บาท) <span>*</span><input name="startPrice" type="number" min="1" required value="${item.startPrice}"></label>
            <label class="field-label">ขั้นต่ำต่อครั้ง (บาท) <span>*</span><input name="minIncrement" type="number" min="1" required value="${item.minIncrement}"></label>
          </div>
          <div class="field-row">
            <label class="field-label">เริ่มประมูล <span>*</span><input name="startAt" type="datetime-local" required value="${toLocal(item.startAt)}"></label>
            <label class="field-label">สิ้นสุดประมูล <span>*</span><input name="endAt" type="datetime-local" required value="${toLocal(item.endAt)}"></label>
          </div>
          <div class="table-actions" style="justify-content:flex-end;">
            <button type="button" class="button button-secondary" data-close-action="1">ยกเลิก</button>
            <button type="submit" class="button button-primary">บันทึกและส่งตรวจใหม่</button>
          </div>
        </form>
      `;
      document.getElementById("editListingImgFile").addEventListener("change", async e => {
        const file = e.currentTarget.files?.[0];
        if (!file) return;
        const fd = new FormData();
        fd.append("file", file);
        try {
          const res = await api("/api/seller/upload-image", { method: "POST", body: fd });
          document.getElementById("editListingImgUrl").value = res.imageUrl;
          showToast(res.message);
        } catch (err) { showToast(err.message, true); }
      });
      document.getElementById("editListingForm").addEventListener("submit", async e => {
        e.preventDefault();
        const fd = new FormData(e.currentTarget);
        try {
          const res = await api(`/api/seller/listings/${item.id}`, {
            method: "PUT",
            body: JSON.stringify({
              name: fd.get("name"),
              categoryId: Number(fd.get("categoryId")),
              condition: fd.get("condition"),
              description: fd.get("description"),
              imageUrl: fd.get("imageUrl"),
              startPrice: Number(fd.get("startPrice")),
              minIncrement: Number(fd.get("minIncrement")),
              startAt: new Date(String(fd.get("startAt"))).toISOString(),
              endAt: new Date(String(fd.get("endAt"))).toISOString()
            })
          });
          showToast(res.message);
          actionDialog.close();
          await loadSellerDashboard();
        } catch (err) { showToast(err.message, true); }
      });
      openDialogSafe(actionDialog);
      return;
    }

    // Seller: Cancel/Delete Pending or Rejected Listing
    const delListBtn = target.closest("[data-delete-listing]");
    if (delListBtn) {
      if (!window.confirm(`ยืนยันยกเลิกรายการสินค้า “${delListBtn.dataset.name}” ใช่หรือไม่?`)) return;
      try {
        const res = await api(`/api/seller/listings/${delListBtn.dataset.deleteListing}`, { method: "DELETE" });
        showToast(res.message);
        await loadSellerDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    if (target.closest("[data-close-action]")) {
      actionDialog.close();
      return;
    }

    const viewLink = target.closest("[data-view-link]");
    if (viewLink) {
      event.preventDefault();
      await switchView(viewLink.dataset.viewLink);
      return;
    }

    if (target.closest("[data-open-auth]")) {
      await openAuthModal("login");
      return;
    }

    if (target.closest("#logoutBtn")) {
      await api("/api/auth/logout", { method: "POST" });
      showToast("ออกจากระบบแล้ว");
      await refreshSession(true);
      return;
    }

    const demoBtn = target.closest("[data-demo-email]");
    if (demoBtn) {
      try {
        const res = await api("/api/auth/login", {
          method: "POST",
          body: JSON.stringify({ email: demoBtn.dataset.demoEmail, password: "123456" })
        });
        authDialog.close();
        showToast(res.message);
        await refreshSession(true);
      } catch (err) { showToast(err.message, true); }
      return;
    }

    const catBtn = target.closest("[data-category]");
    if (catBtn) {
      activeCategoryId = Number(catBtn.dataset.category || 0);
      await loadAuctions().catch(e => showToast(e.message, true));
      return;
    }

    const saveBtn = target.closest("[data-save]");
    if (saveBtn) {
      try {
        const res = await api(`/api/auctions/${saveBtn.dataset.save}/save`, { method: "POST" });
        showToast(res.message);
        if (activeView === "today") await loadAuctions();
        else if (activeView === "mybids") await loadBuyerDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    const detailBtn = target.closest("[data-detail]");
    if (detailBtn) {
      event.preventDefault();
      await openProduct(Number(detailBtn.dataset.detail));
      return;
    }

    const scrollBtn = target.closest("[data-scroll]");
    if (scrollBtn) {
      document.getElementById(scrollBtn.dataset.scroll)?.scrollIntoView({ behavior: "smooth" });
      return;
    }

    // View Payment Slip Modal
    const slipBtn = target.closest("[data-view-slip]");
    if (slipBtn) {
      openSlipModal(slipBtn.dataset.viewSlip, slipBtn.dataset.slipTitle || "หลักฐานการชำระเงิน", slipBtn.dataset.slipNote || "");
      return;
    }

    // Buyer: Open Hybrid Checkout Modal (QR Escrow or Direct to Seller)
    const payBtn = target.closest("[data-pay-order]");
    if (payBtn) {
      const order = buyerOrdersCache.find(o => o.id === Number(payBtn.dataset.payOrder));
      if (!order) return;
      openCheckoutModal(order);
      return;
    }

    // Seller: Verify Direct Payment (Approve / Reject)
    const sellerVerifyBtn = target.closest("[data-seller-verify-pay]");
    if (sellerVerifyBtn) {
      try {
        const res = await api(`/api/seller/orders/${sellerVerifyBtn.dataset.sellerVerifyPay}/verify-payment`, {
          method: "POST", body: JSON.stringify({ approve: true })
        });
        showToast(res.message);
        await loadSellerDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    const sellerRejectPayBtn = target.closest("[data-seller-reject-pay]");
    if (sellerRejectPayBtn) {
      showReasonModal({
        title: `ปฏิเสธการแจ้งชำระเงินตรง (${sellerRejectPayBtn.dataset.code})`,
        subtitle: "ระบบจะแจ้งเหตุผลให้ผู้ซื้อตรวจสอบและส่งใหม่",
        label: "เหตุผล",
        placeholder: "เช่น ยังไม่พบยอดโอนเข้าบัญชี หรือยอดเงินไม่ครบถ้วน",
        submitText: "ยืนยันปฏิเสธ",
        onSubmit: async reason => {
          const res = await api(`/api/seller/orders/${sellerRejectPayBtn.dataset.sellerRejectPay}/verify-payment`, {
            method: "POST", body: JSON.stringify({ approve: false, reason })
          });
          showToast(res.message);
          await loadSellerDashboard();
        }
      });
      return;
    }

    // Finance: Verify Escrow Slip (Approve / Reject)
    const finVerifyBtn = target.closest("[data-finance-verify-pay]");
    if (finVerifyBtn) {
      try {
        const res = await api(`/api/finance/orders/${finVerifyBtn.dataset.financeVerifyPay}/verify-payment`, {
          method: "POST", body: JSON.stringify({ approve: true })
        });
        showToast(res.message);
        await loadFinanceDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    const finRejectPayBtn = target.closest("[data-finance-reject-pay]");
    if (finRejectPayBtn) {
      showReasonModal({
        title: `ปฏิเสธสลิปชำระเงิน (${finRejectPayBtn.dataset.code})`,
        subtitle: "ผู้ซื้อจะได้รับแจ้งเตือนและสามารถอัปโหลดสลิปใหม่ได้",
        label: "เหตุผลในการปฏิเสธสลิป",
        placeholder: "เช่น สลิปไม่ชัดเจน หรือยอดโอนไม่ตรงกับยอดสุทธิ",
        submitText: "ยืนยันปฏิเสธสลิป",
        onSubmit: async reason => {
          const res = await api(`/api/finance/orders/${finRejectPayBtn.dataset.financeRejectPay}/verify-payment`, {
            method: "POST", body: JSON.stringify({ approve: false, reason })
          });
          showToast(res.message);
          await loadFinanceDashboard();
        }
      });
      return;
    }

    // Buyer: Confirm Received
    const recBtn = target.closest("[data-confirm-received]");
    if (recBtn) {
      try {
        const res = await api(`/api/buyer/orders/${recBtn.dataset.confirmReceived}/confirm-received`, { method: "POST" });
        showToast(res.message);
        await loadBuyerDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    // Start Chat or Open Conversation
    const startChatBtn = target.closest("[data-start-chat]");
    if (startChatBtn) {
      try {
        const res = await api("/api/chat/start", {
          method: "POST",
          body: JSON.stringify({ auctionItemId: Number(startChatBtn.dataset.startChat) })
        });
        if (auctionDialog.open) auctionDialog.close();
        activeConversationId = res.conversationId;
        await switchView("messages");
      } catch (err) { showToast(err.message, true); }
      return;
    }

    const openConvBtn = target.closest("[data-open-conv]");
    if (openConvBtn) {
      activeConversationId = Number(openConvBtn.dataset.openConv);
      await switchView("messages");
      return;
    }

    const selConvBtn = target.closest("[data-select-conv]");
    if (selConvBtn) {
      await openConversation(Number(selConvBtn.dataset.selectConv));
      return;
    }

    // Report Dispute from Chat
    const dispBtn = target.closest("[data-report-dispute]");
    if (dispBtn) {
      const convId = Number(dispBtn.dataset.reportDispute);
      const orderId = dispBtn.dataset.orderId ? Number(dispBtn.dataset.orderId) : null;
      document.getElementById("actionDialogContent").innerHTML = `
        <h2 class="auth-title">แจ้งข้อพิพาทให้แอดมินตรวจสอบ</h2>
        <p class="auth-subtitle">เมื่อส่งเรื่องแล้ว แอดมินจะได้รับสิทธิ์อ่านบทสนทนาของเคสนี้เพื่อช่วยไกล่เกลี่ย</p>
        <form id="reportDisputeForm">
          <label class="field-label">หัวข้อปัญหา <span>*</span>
            <select name="reason" required>
              <option value="สินค้าไม่ตรงตามรายละเอียด">สินค้าไม่ตรงตามรายละเอียด</option>
              <option value="ไม่มาตามเวลานัดรับ">ไม่มาตามเวลานัดรับ</option>
              <option value="ปัญหาการส่งมอบหรือการชำระเงิน">ปัญหาการส่งมอบหรือการชำระเงิน</option>
              <option value="อื่นๆ">อื่นๆ</option>
            </select>
          </label>
          <label class="field-label">รายละเอียดข้อพิพาท <span>*</span>
            <textarea name="details" rows="3" required placeholder="อธิบายเหตุการณ์ที่เกิดขึ้นอย่างชัดเจน"></textarea>
          </label>
          <div class="table-actions" style="justify-content:flex-end;">
            <button type="button" class="button button-secondary" data-close-action="1">ยกเลิก</button>
            <button type="submit" class="button button-danger">ส่งเรื่องร้องเรียน</button>
          </div>
        </form>
      `;
      document.getElementById("reportDisputeForm").addEventListener("submit", async e => {
        e.preventDefault();
        const fd = new FormData(e.currentTarget);
        try {
          const res = await api(`/api/chat/conversations/${convId}/dispute`, {
            method: "POST",
            body: JSON.stringify({ reason: fd.get("reason"), details: fd.get("details"), orderPaymentId: orderId })
          });
          showToast(res.message);
          actionDialog.close();
          await loadConversations(convId);
        } catch (err) { showToast(err.message, true); }
      });
      openDialogSafe(actionDialog);
      return;
    }

    // Seller: View Bid History of own item
    const sellerBidsBtn = target.closest("[data-view-seller-bids]");
    if (sellerBidsBtn) {
      const item = sellerListingsCache.find(x => x.id === Number(sellerBidsBtn.dataset.viewSellerBids));
      if (!item) return;
      document.getElementById("actionDialogContent").innerHTML = `
        <h2 class="auth-title">ประวัติการเสนอราคา: ${escapeHtml(item.name)}</h2>
        <p class="auth-subtitle">ราคาเริ่มต้น ${money(item.startPrice)} · ราคาสูงสุดปัจจุบัน ${money(item.currentPrice)} (${item.bidCount} ครั้ง)</p>
        <div class="table-wrap">
          <table class="data-table">
            <thead><tr><th>ผู้เสนอราคา</th><th>จำนวนเงิน</th><th>วันเวลา</th></tr></thead>
            <tbody>
              ${item.bids.length ? item.bids.map(b => `<tr><td>${escapeHtml(b.bidderName)}</td><td class="table-price">${money(b.amount)}</td><td>${formatDateTime(b.createdAt)}</td></tr>`).join("")
                : `<tr class="empty-row"><td colspan="3">ยังไม่มีผู้เสนอราคา</td></tr>`}
            </tbody>
          </table>
        </div>
      `;
      openDialogSafe(actionDialog);
      return;
    }

    // Seller: Schedule Meetup Modal
    const meetupBtn = target.closest("[data-schedule-meetup]");
    if (meetupBtn) {
      const orderId = Number(meetupBtn.dataset.scheduleMeetup);
      const tomorrow = new Date(Date.now() + 24 * 3600000);
      const defVal = new Date(tomorrow.getTime() - tomorrow.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
      document.getElementById("actionDialogContent").innerHTML = `
        <h2 class="auth-title">กำหนดนัดรับสินค้า</h2>
        <p class="auth-subtitle">ระบบจะส่งข้อความแจ้งวันเวลาและสถานที่นัดรับเข้าห้องแชทของผู้ซื้อให้อัตโนมัติ</p>
        <form id="meetupForm">
          <label class="field-label">สถานที่นัดรับ <span>*</span><input name="location" required value="${escapeHtml(meetupBtn.dataset.loc || "")}" placeholder="เช่น BTS อารีย์ ทางออก 3"></label>
          <label class="field-label">วันและเวลานัดรับ <span>*</span><input name="meetupAt" type="datetime-local" required value="${defVal}"></label>
          <label class="field-label">หมายเหตุเพิ่มเติม <textarea name="note" rows="2" placeholder="จุดสังเกตหรือเบอร์ติดต่อ"></textarea></label>
          <div class="table-actions" style="justify-content:flex-end;">
            <button type="button" class="button button-secondary" data-close-action="1">ยกเลิก</button>
            <button type="submit" class="button button-primary">บันทึกนัดรับ</button>
          </div>
        </form>
      `;
      document.getElementById("meetupForm").addEventListener("submit", async e => {
        e.preventDefault();
        const fd = new FormData(e.currentTarget);
        try {
          const res = await api(`/api/seller/orders/${orderId}/meetup`, {
            method: "POST",
            body: JSON.stringify({
              location: fd.get("location"),
              meetupAt: new Date(String(fd.get("meetupAt"))).toISOString(),
              note: fd.get("note")
            })
          });
          showToast(res.message);
          actionDialog.close();
          await loadSellerDashboard();
        } catch (err) { showToast(err.message, true); }
      });
      openDialogSafe(actionDialog);
      return;
    }

    // Seller: Confirm Delivered
    const deliverBtn = target.closest("[data-confirm-deliver]");
    if (deliverBtn) {
      try {
        const res = await api(`/api/seller/orders/${deliverBtn.dataset.confirmDeliver}/deliver`, { method: "POST" });
        showToast(res.message);
        await loadSellerDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    // Admin: Approve/Reject Seller
    const appSellerBtn = target.closest("[data-approve-seller]");
    if (appSellerBtn) {
      try {
        const res = await api(`/api/admin/sellers/${appSellerBtn.dataset.approveSeller}/review`, {
          method: "POST", body: JSON.stringify({ approve: true })
        });
        showToast(res.message);
        await loadAdminDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    const rejSellerBtn = target.closest("[data-reject-seller]");
    if (rejSellerBtn) {
      showReasonModal({
        title: `ปฏิเสธใบสมัครผู้ขาย: ${rejSellerBtn.dataset.name}`,
        subtitle: "เหตุผลนี้จะถูกแสดงให้ผู้สมัครทราบเพื่อแก้ไข",
        label: "เหตุผลการปฏิเสธ",
        placeholder: "เช่น ข้อมูลบัญชีรับเงินไม่ชัดเจน",
        submitText: "ยืนยันปฏิเสธ",
        onSubmit: async reason => {
          const res = await api(`/api/admin/sellers/${rejSellerBtn.dataset.rejectSeller}/review`, {
            method: "POST", body: JSON.stringify({ approve: false, reason })
          });
          showToast(res.message);
          await loadAdminDashboard();
        }
      });
      return;
    }

    // Admin: Approve/Reject Auction
    const appAucBtn = target.closest("[data-approve-auction]");
    if (appAucBtn) {
      try {
        const res = await api(`/api/admin/auctions/${appAucBtn.dataset.approveAuction}/review`, {
          method: "POST", body: JSON.stringify({ approve: true })
        });
        showToast(res.message);
        await loadAdminDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    const rejAucBtn = target.closest("[data-reject-auction]");
    if (rejAucBtn) {
      showReasonModal({
        title: `ปฏิเสธสินค้า: ${rejAucBtn.dataset.name}`,
        subtitle: "ผู้ขายจะเห็นเหตุผลที่ปฏิเสธในหน้าสินค้าของฉัน",
        label: "เหตุผลการปฏิเสธสินค้า",
        placeholder: "เช่น ภาพถ่ายไม่ชัดเจนหรือรายละเอียดไม่ครบถ้วน",
        submitText: "ยืนยันปฏิเสธสินค้า",
        onSubmit: async reason => {
          const res = await api(`/api/admin/auctions/${rejAucBtn.dataset.rejectAuction}/review`, {
            method: "POST", body: JSON.stringify({ approve: false, reason })
          });
          showToast(res.message);
          await loadAdminDashboard();
        }
      });
      return;
    }

    // Admin: Suspend / Unsuspend User
    const suspBtn = target.closest("[data-suspend-user]");
    if (suspBtn) {
      showReasonModal({
        title: `ระงับบัญชี: ${suspBtn.dataset.name}`,
        subtitle: "ผู้ใช้ที่ถูกระงับจะไม่สามารถเข้าสู่ระบบหรือทำรายการได้",
        label: "เหตุผลการระงับบัญชี",
        placeholder: "เช่น ละเมิดกติกาการประมูลซ้ำหลายครั้ง",
        submitText: "ยืนยันระงับบัญชี",
        onSubmit: async reason => {
          const res = await api(`/api/admin/users/${suspBtn.dataset.suspendUser}/suspend`, {
            method: "POST", body: JSON.stringify({ suspend: true, reason })
          });
          showToast(res.message);
          await loadAdminDashboard();
        }
      });
      return;
    }

    const unsuspBtn = target.closest("[data-unsuspend-user]");
    if (unsuspBtn) {
      try {
        const res = await api(`/api/admin/users/${unsuspBtn.dataset.unsuspendUser}/suspend`, {
          method: "POST", body: JSON.stringify({ suspend: false })
        });
        showToast(res.message);
        await loadAdminDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    // Admin: Edit / Delete Category
    const editCatBtn = target.closest("[data-edit-cat]");
    if (editCatBtn) {
      const form = document.getElementById("adminCategoryForm");
      form.id.value = editCatBtn.dataset.editCat;
      form.name.value = editCatBtn.dataset.catName;
      form.symbol.value = editCatBtn.dataset.catSym;
      form.description.value = editCatBtn.dataset.catDesc;
      document.getElementById("catSubmitBtn").textContent = "บันทึกการแก้ไข";
      document.getElementById("catCancelEditBtn").classList.remove("hidden");
      form.scrollIntoView({ behavior: "smooth" });
      return;
    }

    const delCatBtn = target.closest("[data-delete-cat]");
    if (delCatBtn) {
      try {
        const res = await api(`/api/admin/categories/${delCatBtn.dataset.deleteCat}`, { method: "DELETE" });
        showToast(res.message);
        await loadAdminDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    // Admin: Inspect Dispute Chat
    const inspDispBtn = target.closest("[data-inspect-dispute]");
    if (inspDispBtn) {
      await openDisputeInspector(Number(inspDispBtn.dataset.inspectDispute));
      return;
    }

    // Finance: Release Payout
    const relBtn = target.closest("[data-finance-release]");
    if (relBtn) {
      showReasonModal({
        title: `ปล่อยเงินให้ผู้ขาย (${relBtn.dataset.code})`,
        subtitle: `ยอดโอนสุทธิ ${money(relBtn.dataset.amt)} — ตรวจสอบแล้วว่ามีการส่งมอบ/รับของเรียบร้อย`,
        label: "หมายเหตุการโอนเงิน (เช่น เลขอ้างอิงสลิป)",
        placeholder: "โอนเงินเข้าบัญชีผู้ขายเรียบร้อยแล้ว",
        submitText: "ยืนยันปล่อยเงิน",
        danger: false,
        onSubmit: async note => {
          const res = await api(`/api/finance/orders/${relBtn.dataset.financeRelease}/release`, {
            method: "POST", body: JSON.stringify({ note })
          });
          showToast(res.message);
          await loadFinanceDashboard();
        }
      });
      return;
    }

    // Finance: Refund Buyer
    const refBtn = target.closest("[data-finance-refund]");
    if (refBtn) {
      showReasonModal({
        title: `คืนเงินให้ผู้ซื้อ (${refBtn.dataset.code})`,
        subtitle: `ยอดคืนเงิน ${money(refBtn.dataset.amt)}`,
        label: "เหตุผลและหมายเหตุการคืนเงิน",
        placeholder: "คืนเงินเต็มจำนวนตามผลการพิจารณาข้อพิพาท",
        submitText: "ยืนยันคืนเงิน",
        danger: true,
        onSubmit: async note => {
          const res = await api(`/api/finance/orders/${refBtn.dataset.financeRefund}/refund`, {
            method: "POST", body: JSON.stringify({ note })
          });
          showToast(res.message);
          await loadFinanceDashboard();
        }
      });
      return;
    }
  });

  // Live countdown ticker based on real UTC ISO endAt
  let expirySyncPending = false;
  window.setInterval(() => {
    let anyExpiredJustNow = false;

    document.querySelectorAll("[data-end-at]").forEach(el => {
      if (el.tagName === "FORM") return;
      const expired = parseUtcDate(el.dataset.endAt).getTime() <= Date.now();
      const prevText = el.textContent;
      el.textContent = expired ? "ปิดประมูลแล้ว" : remainingText(el.dataset.endAt);
      if (expired && prevText !== "ปิดประมูลแล้ว") {
        anyExpiredJustNow = true;
      }
    });

    // Immediately lock bid form inside open dialog once expired
    const openBidForm = document.getElementById("bidForm");
    if (openBidForm && openBidForm.dataset.endAt && parseUtcDate(openBidForm.dataset.endAt).getTime() <= Date.now()) {
      openBidForm.querySelectorAll("input, button").forEach(ctrl => { ctrl.disabled = true; });
      const btn = openBidForm.querySelector("button[type='submit']");
      if (btn) btn.textContent = "ปิดประมูลแล้ว";
      anyExpiredJustNow = true;
    }

    if (anyExpiredJustNow && !expirySyncPending) {
      expirySyncPending = true;
      window.setTimeout(async () => {
        try {
          if (auctionDialog.open && selectedProductId) await openProduct(selectedProductId);
          if (activeView === "today") await loadAuctions();
        } catch {}
        expirySyncPending = false;
      }, 600);
    }
  }, 1000);

  // Boot
  document.getElementById("todayDate").textContent = new Intl.DateTimeFormat("th-TH", {
    weekday: "long", day: "numeric", month: "long", year: "numeric"
  }).format(new Date());
  initListingDates();
  refreshSession(true).catch(err => showToast(err.message, true));
})();
