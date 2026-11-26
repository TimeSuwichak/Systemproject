/**
 * ============================================================================
 * ไฟล์: AppProject/wwwroot/js/site.js
 * บทบาท: ฝั่งหน้าบ้าน (Frontend - Main Client-Side SPA Controller)
 * ============================================================================
 * หน้าที่โดยรวม:
 * - เป็นสคริปต์หลักเพียงไฟล์เดียวที่ควบคุมการทำงานของหน้าเว็บทั้งหมดในรูปแบบ
 *   SPA (Single Page Application) สำหรับระบบประมูลของสะสม "OldThings / ของเก่าเล่าเรื่อง"
 * - ถูกโหลดจากหน้า `Views/Home/Index.cshtml` (ผ่าน Layout หรือ script tag)
 * - จัดการสถานะผู้ใช้ (Session / Role ทั้ง 4 บทบาท: Buyer, Seller, Admin, Finance),
 *   การสลับหน้าจอ (View Panels) โดยไม่ต้องรีโหลดหน้าเว็บ, การเรนเดอร์ตาราง/การ์ด/Modal,
 *   ระบบแชทและการแจ้งเตือนแบบ Polling ทุก 5 วินาที รวมถึงตัวนับเวลาถอยหลัง (Countdown)
 *
 * การเชื่อมต่อกับไฟล์/เลเยอร์อื่น:
 * - เชื่อมกับหน้า HTML/Razor: `Views/Home/Index.cshtml` (อ้างอิง DOM id, form, dialog, data-* attributes)
 * - เชื่อมกับสไตล์ชีต: `wwwroot/css/site.css` (สลับ class เช่น .active, .hidden, .list-view, .status-pill)
 * - เชื่อมกับฝั่งหลังบ้าน (ASP.NET Core Web API Controllers):
 *   สื่อสารผ่านฟังก์ชันกลาง `api(url, options)` ที่ครอบ `fetch()` และแนบ Cookie Authentication
 *   (`credentials: "same-origin"`) ไปยัง Controllers ได้แก่:
 *   AuthController, AuctionsController, SellerController, ChatController,
 *   AdminController, FinanceController, NotificationsController, AuditLogsController
 * ============================================================================
 */
(() => {
  // ==================== 1. ค่าคงที่และแผนที่ข้อความ (CONSTANTS & LOOKUP MAPS) ====================
  // รูปภาพสำรองกรณี URL รูปสินค้าหรือรูปสลิปเสียหรือไม่ถูกต้อง
  const imageFallback = "https://images.unsplash.com/photo-1577083552431-6e5fd01aa342?auto=format&fit=crop&w=800&q=80";

  // แปลงชื่อ Role จากฝั่งหลังบ้าน (ภาษาอังกฤษ) เป็นป้ายชื่อภาษาไทยสำหรับแสดงผลบน UI
  const roleLabels = {
    Buyer: "ผู้ประมูล",
    Seller: "ผู้ขาย",
    Admin: "แอดมิน",
    Finance: "การเงิน"
  };

  // แผนที่ชื่อหัวข้อ (Breadcrumb / Page Title) ของแต่ละหน้าจอ (View Panel) ใน SPA
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

  // ==================== 2. สถานะส่วนกลางของแอปฝั่งหน้าบ้าน (GLOBAL CLIENT STATE) ====================
  let currentUser = null; // ข้อมูลผู้ใช้ที่ล็อกอินอยู่ (ได้จาก GET /api/auth/me) หรือ null หากยังไม่เข้าสู่ระบบ
  let globalSettings = { sameCategoryDiscountPercent: 10, platformFeePercent: 5, globalMinBidIncrement: 50 }; // กติการะบบล่าสุดจากฝ่ายการเงิน
  let activeView = "today"; // ชื่อ view ปัจจุบันที่กำลังเปิดแสดงอยู่ (ตรงกับ id="view-{activeView}")
  let activeCategoryId = 0; // รหัสหมวดหมู่ที่เลือกกรองในหน้าประมูลวันนี้ (0 = แสดงทั้งหมด)
  let sortMode = "ending"; // โหมดเรียงลำดับสินค้า ("ending", "newest", "price_asc", "price_desc" ฯลฯ)
  let searchTerm = ""; // คำค้นหาจากช่องค้นหาด้านบน (Global Search)
  let listViewMode = false; // สลับการแสดงผลการ์ดสินค้าแบบ Grid (false) หรือ List (true)
  let liveProducts = []; // แคชรายการสินค้าประมูลที่โหลดมาล่าสุด
  let cachedCategories = []; // แคชหมวดหมู่สินค้า (ใช้ซ้ำในฟอร์มลงขาย/แก้ไขสินค้าเพื่อลดการยิง API)
  let sellerListingsCache = []; // แคชสินค้าที่ผู้ขายลงขายไว้ (ใช้เปิด Modal แก้ไขและดูประวัติบิด)
  let buyerOrdersCache = []; // แคชคำสั่งซื้อที่ผู้ซื้อชนะประมูล (ใช้เปิด Modal ชำระเงิน)
  let buyerPaymentConfig = { platformPromptPayId: "081-999-8888", platformAccountName: "บจก. ของเก่าเล่าเรื่อง (บัญชีกลาง)" }; // ข้อมูลบัญชีกลาง Escrow สำหรับสร้าง QR
  let conversationsCache = []; // แคชรายการห้องแชททั้งหมดของผู้ใช้ปัจจุบัน
  let activeConversationId = null; // รหัสห้องแชทที่กำลังเปิดอ่านอยู่
  let selectedProductId = null; // รหัสสินค้าที่กำลังเปิดดูใน Modal รายละเอียดสินค้า (ใช้ตอนส่งฟอร์มเสนอราคา)
  let demoAccountsCache = []; // แคชบัญชีทดลอง 4 บทบาท (โหลดจาก GET /api/auth/demo-accounts)
  let unreadMessagesCount = 0; // จำนวนข้อความแชทที่ยังไม่อ่าน (แสดงเป็น Badge บนเมนู Sidebar)
  let lastNotifiedMessageId = null; // รหัสข้อความล่าสุดที่แจ้งเตือนไปแล้ว เพื่อตรวจจับข้อความเข้าใหม่แบบ Real-time
  let notifPollTimer = null; // ตัวเก็บ ID ของ setInterval สำหรับ Polling แจ้งเตือนทุก 5 วินาที

  // ==================== 3. การอ้างอิง DOM หลักที่ใช้ร่วมกัน (SHARED DOM REFERENCES) ====================
  const toastRegion = document.getElementById("toastRegion"); // พื้นที่แสดงข้อความแจ้งเตือนแบบ Toast มุมจอ
  const auctionDialog = document.getElementById("auctionDialog"); // <dialog> แสดงรายละเอียดสินค้าและฟอร์มเสนอราคา
  const authDialog = document.getElementById("authDialog"); // <dialog> สำหรับเข้าสู่ระบบ สมัครสมาชิก และลืมรหัสผ่าน
  const actionDialog = document.getElementById("actionDialog"); // <dialog> อเนกประสงค์ (จ่ายเงิน, ดูสลิป, แก้ไขสินค้า, กรอกเหตุผล, ตรวจข้อพิพาท)

  // ==================== 4. ฟังก์ชันช่วยเหลือทั่วไป (UTILITY HELPERS) ====================
  /**
   * แสดงข้อความแจ้งเตือนชั่วคราว (Toast Notification) บนหน้าจอ
   * @param {string} message - ข้อความที่ต้องการแสดง
   * @param {boolean} isError - true หากเป็นข้อความแจ้งข้อผิดพลาด (แสดงธีมสีแดง)
   * @param {Function|null} onClick - callback เมื่อผู้ใช้คลิกที่ Toast (เช่น คลิกแล้วเปิดห้องแชท)
   */
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
    // ตั้งเวลาลบ Toast อัตโนมัติ (ถ้าคลิกได้จะให้อยู่นานกว่าเล็กน้อย 6 วินาที, ปกติ 3.8 วินาที)
    window.setTimeout(() => toast.remove(), onClick ? 6000 : 3800);
  }

  /**
   * ป้องกันช่องโหว่ XSS (Cross-Site Scripting) โดยแปลงอักขระพิเศษของ HTML
   * ก่อนนำข้อมูลจากผู้ใช้หรือ API ไปแทรกใน Template Literal (`innerHTML`)
   */
  function escapeHtml(value) {
    return String(value ?? "").replace(/[&<>"']/g, ch => ({
      "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
    })[ch]);
  }

  /**
   * ตรวจสอบความปลอดภัยของ URL รูปภาพ อนุญาตเฉพาะโปรโตคอล http: และ https:
   * (ป้องกัน javascript: URL) หากไม่ถูกต้องจะคืนรูปภาพสำรอง (imageFallback)
   */
  function safeImageUrl(value) {
    try {
      const url = new URL(value || "", window.location.origin);
      return url.protocol === "https:" || url.protocol === "http:" ? url.href : imageFallback;
    } catch {
      return imageFallback;
    }
  }

  /**
   * จัดรูปแบบตัวเลขเป็นสกุลเงินบาทไทย เช่น ฿1,250.5
   */
  function money(value) {
    return `฿${Number(value || 0).toLocaleString("th-TH", { maximumFractionDigits: 2 })}`;
  }

  /**
   * แปลงสตริงวันเวลา ISO จากฝั่งหลังบ้าน (.NET DateTime) ให้เป็นอ็อบเจกต์ Date แบบ UTC
   * หากสตริงไม่มีตัวระบุ Timezone (Z หรือ +07:00) จะต่อท้าย "Z" เพื่อบังคับตีความเป็น UTC ก่อนแปลงเป็นเวลาท้องถิ่น
   */
  function parseUtcDate(iso) {
    if (!iso) return new Date(NaN);
    const s = String(iso).trim();
    const hasTz = /(?:Z|[+-]\d{2}:\d{2})$/i.test(s);
    return new Date(hasTz ? s : `${s}Z`);
  }

  /**
   * จัดรูปแบบวันและเวลาภาษาไทยแบบครบถ้วน เช่น "27 พ.ย. 69 14:30"
   */
  function formatDateTime(iso) {
    if (!iso) return "-";
    const d = parseUtcDate(iso);
    if (Number.isNaN(d.getTime())) return "-";
    return new Intl.DateTimeFormat("th-TH", {
      day: "numeric", month: "short", year: "2-digit", hour: "2-digit", minute: "2-digit"
    }).format(d);
  }

  /**
   * จัดรูปแบบวันที่ภาษาไทยแบบสั้น (ไม่แสดงเวลา) เช่น "27 พ.ย. 69"
   */
  function formatDateShort(iso) {
    if (!iso) return "-";
    const d = parseUtcDate(iso);
    if (Number.isNaN(d.getTime())) return "-";
    return new Intl.DateTimeFormat("th-TH", { day: "numeric", month: "short", year: "2-digit" }).format(d);
  }

  /**
   * คำนวณเวลาคงเหลือจนถึงเวลาปิดประมูล (endAtIso) เพื่อแสดงบนตัวนับถอยหลัง
   * - ถ้าเกินเวลาแล้วคืน "ปิดประมูลแล้ว"
   * - ถ้าเหลือตั้งแต่ 24 ชม. ขึ้นไปแสดงเป็น "X วัน Y ชม."
   * - ถ้าน้อยกว่า 24 ชม. แสดงเป็น "HH:MM:SS"
   */
  function remainingText(endAtIso) {
    const diffSec = Math.floor((parseUtcDate(endAtIso).getTime() - Date.now()) / 1000);
    if (Number.isNaN(diffSec) || diffSec <= 0) return "ปิดประมูลแล้ว";
    const hours = Math.floor(diffSec / 3600);
    const minutes = Math.floor((diffSec % 3600) / 60);
    const secs = diffSec % 60;
    if (hours >= 24) return `${Math.floor(hours / 24)} วัน ${hours % 24} ชม.`;
    return `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}:${String(secs).padStart(2, "0")}`;
  }

  // ============================================================================
  // 🔗 [จุดเชื่อมต่อ หน้าบ้าน <-> หลังบ้าน] จุดเชื่อมต่อหลัก (Central HTTP Bridge)
  // ============================================================================
  // ฟังก์ชันกลาง `api(url, options)` คือสะพานเชื่อมต่อหลักเพียงจุดเดียวระหว่างฝั่งหน้าบ้าน (site.js)
  // กับฝั่งหลังบ้าน (ASP.NET Core Web API Controllers ภายใต้เส้นทาง `/api/...`)
  // หลักการทำงาน:
  // 1. กำหนด Header `Content-Type: application/json` อัตโนมัติเมื่อส่ง body ที่ไม่ใช่ FormData
  //    (กรณีส่ง FormData สำหรับอัปโหลดรูปภาพ/สลิป จะปล่อยให้ Browser ตั้ง multipart/form-data เอง)
  // 2. เรียก `fetch(url, ...)` พร้อม `credentials: "same-origin"` เพื่อส่ง Auth Cookie ของ ASP.NET Core
  //    แนบไปยืนยันตัวตนกับหลังบ้านทุกคำขอ (Cookie Authentication)
  // 3. แปลง Response text เป็น JSON อย่างปลอดภัย (ไม่พังหากหลังบ้านคืนค่าว่าง)
  // 4. หาก HTTP Status ไม่ใช่ 2xx (`!res.ok`) จะดึงข้อความ `data.message` จากหลังบ้านมาโยนเป็น Error
  //    ให้ผู้เรียกนำไปแสดงใน Toast ต่อไป
  async function api(url, options = {}) {
    const headers = { ...(options.headers || {}) };
    if (options.body && !(options.body instanceof FormData)) {
      headers["Content-Type"] = "application/json";
    }
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน] ยิง HTTP Request จริงผ่าน Fetch API พร้อมแนบ Cookie ยืนยันตัวตน
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

  // ==================== 5. ฟังก์ชันช่วยสร้าง UI, ป้ายสถานะ และ MODAL ชำระเงิน ====================
  /**
   * เปิด HTML5 `<dialog>` อย่างปลอดภัย (ป้องกัน Error กรณีเรียก showModal() ซ้ำตอนเปิดอยู่แล้ว)
   */
  function openDialogSafe(dialog) {
    if (!dialog.open) dialog.showModal();
  }

  /**
   * ปิดแถบเมนูด้านข้าง (Sidebar Drawer) บนหน้าจอมือถือและซ่อนฉากหลังมืด (Backdrop)
   */
  function closeMobileDrawer() {
    document.getElementById("sidebar").classList.remove("mobile-open");
    document.getElementById("sidebarBackdrop").classList.add("hidden");
  }

  /**
   * สร้างป้ายสถานะ HTML (Status Pill) ตามธีมสี: green, pending, red, blue, muted
   */
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

  /**
   * แปลงสถานะการนัดรับ/ส่งมอบสินค้า (`NotScheduled`, `Scheduled`, `Delivered`, `Completed`) เป็นป้ายภาษาไทย
   */
  function meetupPill(status) {
    switch (status) {
      case "Completed": return statusPill("green", "ผู้ซื้อยืนยันรับของแล้ว");
      case "Delivered": return statusPill("blue", "ผู้ขายส่งมอบแล้ว");
      case "Scheduled": return statusPill("pending", "กำหนดนัดรับแล้ว");
      default: return statusPill("muted", "ยังไม่กำหนดนัดรับ");
    }
  }

  /**
   * แปลงรหัสวิธีชำระเงินเป็นข้อความภาษาไทย (`DirectToSeller` vs `PlatformEscrow`)
   */
  function paymentMethodLabel(method) {
    return method === "DirectToSeller" ? "จ่ายตรงผู้ขาย" : "QR ระบบกลาง (Escrow)";
  }

  /**
   * แปลงสถานะการชำระเงิน (`Unpaid`, `PendingVerification`, `Paid`, `Refunded`, `Cancelled`)
   * เป็นป้ายสถานะ โดยแยกข้อความกรณีรอตรวจสลิปฝ่ายการเงิน vs รอผู้ขายยืนยันจ่ายตรง
   */
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

  /**
   * แปลงสถานะการปล่อยเงิน/โอนเงินให้ผู้ขาย (`Pending`, `Released`, `DirectSettled`, `Refunded`, `Cancelled`)
   */
  function payoutPill(status) {
    switch (status) {
      case "Released": return statusPill("green", "ปล่อยเงินให้ผู้ขายแล้ว");
      case "DirectSettled": return statusPill("blue", "ผู้ขายรับเงินตรงแล้ว");
      case "Refunded": return statusPill("red", "คืนเงินผู้ซื้อแล้ว");
      case "Cancelled": return statusPill("muted", "ยกเลิกแล้ว");
      default: return statusPill("pending", "รอปล่อยเงิน");
    }
  }

  /**
   * สร้างภาพ QR Code จำลองแบบ SVG (Deterministic) จากข้อความ Seed (เลขพร้อมเพย์|รหัสออเดอร์|ยอดเงิน)
   * เพื่อให้แต่ละคำสั่งซื้อมีลวดลาย QR เฉพาะตัวในหน้าชำระเงินระบบกลาง (Escrow) โดยไม่ต้องพึ่งไลบรารีภายนอก
   */
  function buildPromptPayQrSvg(seedText) {
    const size = 21; // ขนาดตาราง QR รุ่น 1 (21x21 โมดูล)
    // คำนวณค่า Hash เริ่มต้นแบบ FNV-1a จาก seedText
    let hash = 2166136261;
    for (let i = 0; i < seedText.length; i++) {
      hash ^= seedText.charCodeAt(i);
      hash = Math.imul(hash, 16777619) >>> 0;
    }
    // ตรวจว่าเป็นตำแหน่งกรอบสี่เหลี่ยมจับมุม 3 มุม (Finder Patterns) หรือไม่
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
          // เส้นจังหวะสลับสี (Timing Pattern) แถวที่ 6 และคอลัมน์ที่ 6
          on = (r + c) % 2 === 0;
        } else {
          // สุ่มจุดข้อมูลแบบ Deterministic ด้วย Linear Congruential Generator (LCG)
          hash = (Math.imul(hash ^ (r * 31 + c), 1103515245) + 12345) >>> 0;
          on = ((hash >> 16) & 1) === 1;
        }
        if (on) rects += `<rect x="${c}" y="${r}" width="1" height="1" fill="#182a20"/>`;
      }
    }
    return `<svg viewBox="0 0 ${size} ${size}" xmlns="http://www.w3.org/2000/svg" role="img" aria-label="PromptPay QR"><rect width="${size}" height="${size}" fill="#fff"/>${rects}</svg>`;
  }

  /**
   * เปิด Modal (`actionDialog`) แสดงภาพสลิปหลักฐานการโอนเงินขนาดใหญ่ พร้อมปุ่มเปิดดูในแท็บใหม่
   */
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

  /**
   * เปิด Modal ชำระเงินแบบผสม (Hybrid Checkout) สำหรับผู้ซื้อที่ชนะประมูล
   * รองรับ 2 ช่องทาง:
   * 1) `PlatformEscrow`: สแกน QR ระบบกลาง + บังคับแนบไฟล์สลิป ให้ฝ่ายการเงินตรวจสอบ
   * 2) `DirectToSeller`: โอนตรง/นัดจ่ายเงินสดกับผู้ขาย (ไม่บังคับแนบสลิป) ให้ผู้ขายกดยืนยันเอง
   */
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

    // สลับ UI ระหว่างโหมดจ่ายผ่าน QR ระบบกลาง (Escrow) กับโหมดจ่ายตรงผู้ขาย
    form.querySelectorAll("input[name='paymentMethod']").forEach(radio => {
      radio.addEventListener("change", () => {
        const isEscrow = radio.value === "PlatformEscrow";
        form.querySelectorAll(".pay-method-card").forEach(card => {
          card.classList.toggle("selected", card.dataset.payCard === radio.value);
        });
        document.getElementById("checkoutEscrowBox").classList.toggle("hidden", !isEscrow);
        document.getElementById("checkoutDirectBox").classList.toggle("hidden", isEscrow);
        // บังคับแนบสลิปเฉพาะโหมด PlatformEscrow เท่านั้น
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

    // แสดงภาพตัวอย่าง (Preview) สลิปทันทีที่ผู้ใช้เลือกไฟล์จากเครื่อง
    slipInput.addEventListener("change", () => {
      const file = slipInput.files?.[0];
      if (file) {
        previewImg.src = URL.createObjectURL(file);
        previewWrap.classList.remove("hidden");
      } else {
        previewWrap.classList.add("hidden");
      }
    });

    // ส่งข้อมูลการชำระเงินและไฟล์สลิปไปยังฝั่งหลังบ้านเมื่อกดปุ่มยืนยัน
    form.addEventListener("submit", async e => {
      e.preventDefault();
      const fd = new FormData(form);
      try {
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
        // Endpoint: POST /api/buyer/orders/{order.id}/pay -> AuctionsController.PayOrder
        // ข้อมูลที่ส่ง: multipart/form-data (`FormData`) ประกอบด้วย paymentMethod, slipFile (ไฟล์รูป), note
        // ผลลัพธ์จากหลังบ้าน: JSON `{ message }` บันทึกไฟล์สลิปและเปลี่ยนสถานะเป็น PendingVerification -> ปิด Modal, แสดง Toast และรีโหลดหน้าแดชบอร์ดผู้ซื้อ
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

  /**
   * แปลงสถานะสินค้าประมูล (`Approved`, `Pending`, `Rejected`, `Closed`) เป็นป้ายสถานะภาษาไทย
   */
  function auctionStatusPill(status) {
    switch (status) {
      case "Approved": return statusPill("green", "เปิดประมูล");
      case "Pending": return statusPill("pending", "รอแอดมินอนุมัติ");
      case "Rejected": return statusPill("red", "ถูกปฏิเสธ");
      case "Closed": return statusPill("muted", "ปิดประมูลแล้ว");
      default: return statusPill("muted", status);
    }
  }

  /**
   * กำหนดค่าเริ่มต้นของช่องวันเวลาเริ่มประมูล (+5 นาที) และสิ้นสุดประมูล (+24 ชั่วโมง)
   * ในฟอร์มลงขายสินค้า `#listingForm` โดยปรับชดเชย Timezone ให้เข้ากับ `<input type="datetime-local">`
   */
  function initListingDates() {
    const now = new Date();
    const toLocalInput = d => new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
    const startInput = document.querySelector("#listingForm [name='startAt']");
    const endInput = document.querySelector("#listingForm [name='endAt']");
    if (startInput) startInput.value = toLocalInput(new Date(now.getTime() + 5 * 60000));
    if (endInput) endInput.value = toLocalInput(new Date(now.getTime() + 24 * 3600000));
  }

  // ==================== 6. การนำทาง, เมนูด้านข้าง และ UI ยืนยันตัวตน (NAVIGATION & AUTH UI) ====================
  /**
   * สร้างเมนู Sidebar ด้านซ้ายและแถบข้อมูลผู้ใช้ด้านบน (Topbar) แบบไดนามิกตาม Role ปัจจุบัน:
   * - Guest / Buyer: ดูตลาดประมูล, รายการที่สนใจ, ชำระเงิน, แชท, สมัครเป็นผู้ขาย
   * - Seller: เมนูผู้ขาย (ลงขาย, นัดรับ & ยอดขาย, แชท) + เมนูประมูลซื้อของสะสม
   * - Admin: อนุมัติผู้ขาย/สินค้า, จัดการผู้ใช้, หมวดหมู่, ข้อพิพาท, ประวัติผู้ชนะ, Audit Log
   * - Finance: ตรวจสลิป/ปล่อยเงิน/คืนเงิน, ตั้งกติกาส่วนลด/ค่าธรรมเนียม, รายงานยอดขาย, Audit Log
   */
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

    // เรนเดอร์หมวดหมู่เมนูและปุ่มเปลี่ยน View พร้อม Badge นับข้อความแชทที่ยังไม่อ่าน
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

    // อัปเดตส่วนโปรไฟล์มุมล่างซ้ายของ Sidebar, ปุ่มบน Topbar และปุ่ม Hero ตามสถานะการล็อกอิน
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

  /**
   * สลับหน้าจอ SPA ไปยัง View เป้าหมาย (ซ่อน Panel อื่น แสดง `#view-${view}`),
   * อัปเดต Breadcrumb, แสดง/ซ่อนช่องค้นหาสินค้า และเรียกฟังก์ชันโหลดข้อมูล API ของหน้านั้นๆ
   */
  async function switchView(view) {
    if (!pageTitles[view]) return;
    activeView = view;
    closeMobileDrawer();

    document.querySelectorAll(".view-panel").forEach(p => p.classList.toggle("active", p.id === `view-${view}`));
    document.querySelectorAll("[data-view-link]").forEach(btn => btn.classList.toggle("active", btn.dataset.viewLink === view));
    document.getElementById("pageCrumb").textContent = pageTitles[view];
    // แสดงช่องค้นหาเฉพาะหน้าตลาดประมูลวันนี้ ("today")
    document.getElementById("globalSearchBox").classList.toggle("hidden", view !== "today");
    window.scrollTo({ top: 0, behavior: "smooth" });

    // โหลดข้อมูลล่าสุดจากฝั่งหลังบ้านให้สอดคล้องกับหน้าที่เปิดอยู่
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

  // ==================== 7. MODAL ยืนยันตัวตน, AUDIT LOG, SESSION & POLLING ====================
  /**
   * เปิด Modal เข้าสู่ระบบ / สมัครสมาชิก / ลืมรหัสผ่าน พร้อมโหลดรายชื่อบัญชีทดลอง 4 บทบาทมาแสดง (แคชไว้ครั้งแรก)
   */
  async function openAuthModal(tab = "login") {
    document.querySelectorAll(".auth-tab").forEach(t => t.classList.toggle("active", t.dataset.authTab === tab));
    document.getElementById("authTabLogin").classList.toggle("hidden", tab !== "login");
    document.getElementById("authTabRegister").classList.toggle("hidden", tab !== "register");
    document.getElementById("authTabForgot")?.classList.toggle("hidden", tab !== "forgot");

    const demoSection = document.getElementById("demoAccountsSection");
    if (demoAccountsCache.length === 0) {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: GET /api/auth/demo-accounts -> AuthController.DemoAccounts
      // ข้อมูลที่ส่ง: ไม่มี (GET เปล่า)
      // ผลลัพธ์จากหลังบ้าน: JSON Array ของบัญชีทดลอง `[{ email, role, label, name, note }]` (ว่างใน Production) -> นำมาสร้างการ์ดคลิกสลับบัญชีใน `#demoAccountsGrid`
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

  /**
   * โหลดตารางบันทึกประวัติการทำงาน (Audit Log) สำหรับ Admin และ Finance พร้อมตัวกรองตาม Role
   */
  async function loadAuditLogs(roleFilter = "all") {
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
    // Endpoint: GET /api/audit-logs?role={roleFilter} -> AuditLogsController.GetLogs
    // ข้อมูลที่ส่ง: Query String `role` (เช่น "all", "Admin", "Finance", "Seller", "Buyer")
    // ผลลัพธ์จากหลังบ้าน: JSON Array ของรายการ AuditLog -> เรนเดอร์ลงตาราง `#auditLogRows`
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

  /**
   * ตรวจสอบ Session ปัจจุบันจาก Auth Cookie กับฝั่งหลังบ้าน, อัปเดต `currentUser` และ `globalSettings`,
   * สร้าง Sidebar ใหม่, พาไปยังหน้าแรกตามบทบาท (ถ้า `goDefaultView = true`) และเริ่ม Timer แจ้งเตือนทุก 5 วินาที
   */
  async function refreshSession(goDefaultView = false) {
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
    // Endpoint: GET /api/auth/me -> AuthController.Me
    // ข้อมูลที่ส่ง: Auth Cookie อัตโนมัติ (`credentials: "same-origin"`)
    // ผลลัพธ์จากหลังบ้าน: JSON `{ authenticated, user, settings }` -> อัปเดตสถานะผู้ใช้ กติการะบบ และ UI ทั้งหน้า
    const me = await api("/api/auth/me");
    const prevUserId = currentUser?.id || null;
    currentUser = me.authenticated ? me.user : null;
    if (me.settings) globalSettings = me.settings;

    // หากมีการสลับบัญชีหรือออกจากระบบ ให้รีเซ็ตสถานะแชทและตัวนับแจ้งเตือนของคนเดิม
    if ((currentUser?.id || null) !== prevUserId) {
      lastNotifiedMessageId = null;
      unreadMessagesCount = 0;
      activeConversationId = null;
    }

    const notifWrapper = document.getElementById("notifWrapper");
    if (notifWrapper) notifWrapper.classList.toggle("hidden", !currentUser);
    document.getElementById("notifDropdown")?.classList.add("hidden");

    buildSidebar();

    // ยกเลิกตัว Polling เดิมก่อนเริ่มรอบใหม่เพื่อป้องกัน Timer ซ้อนกัน
    if (notifPollTimer) {
      window.clearInterval(notifPollTimer);
      notifPollTimer = null;
    }

    if (goDefaultView) {
      // กำหนดหน้าแรก (Landing View) ของแต่ละบทบาทหลังล็อกอิน
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

    // หากล็อกอินอยู่ ให้เริ่มดึงสรุปการแจ้งเตือนทันทีและตั้ง Polling ซ้ำทุก 5 วินาที
    if (currentUser) {
      await pollNotifications(true);
      notifPollTimer = window.setInterval(() => pollNotifications(false), 5000);
    }
  }

  /**
   * ระบบ Polling ดึงสรุปการแจ้งเตือนและข้อความแชทเข้าใหม่จากฝั่งหลังบ้านทุก 5 วินาที
   * @param {boolean} isInitial - true ในการเรียกครั้งแรกหรือหลังอ่านแจ้งเตือน (จะไม่เด้ง Toast ซ้ำ)
   */
  async function pollNotifications(isInitial = false) {
    if (!currentUser) return;
    try {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: GET /api/notifications/summary -> NotificationsController.GetSummary
      // ข้อมูลที่ส่ง: Auth Cookie อัตโนมัติ
      // ผลลัพธ์จากหลังบ้าน: JSON `{ unreadMessagesCount, totalCount, roleAlerts, notifications, latestMessageId, latestIncomingMessage }`
      // -> อัปเดต Badge กระดิ่ง/เมนูแชท, เรนเดอร์ Dropdown แจ้งเตือน และเด้ง Toast/รีเฟรชห้องแชทเมื่อมีข้อความเข้าใหม่
      const data = await api("/api/notifications/summary");
      unreadMessagesCount = data.unreadMessagesCount || 0;

      // อัปเดตตัวเลข Badge จำนวนข้อความที่ยังไม่อ่านบนเมนู Sidebar
      const navBadge = document.getElementById("navUnreadMessagesBadge");
      if (navBadge) {
        navBadge.textContent = unreadMessagesCount > 99 ? "99+" : String(unreadMessagesCount);
        navBadge.classList.toggle("hidden", unreadMessagesCount <= 0);
      }

      // อัปเดตตัวเลข Badge บนปุ่มกระดิ่งแจ้งเตือนที่ Topbar
      const bellBadge = document.getElementById("notifBadge");
      const total = data.totalCount || 0;
      if (bellBadge) {
        bellBadge.textContent = total > 99 ? "99+" : String(total);
        bellBadge.classList.toggle("hidden", total <= 0);
      }

      // เรนเดอร์รายการแจ้งเตือนใน Dropdown (รวมงานค้างตามบทบาท `roleAlerts` + แจ้งเตือนรายบุคคล `notifications`)
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

      // ตรวจจับข้อความแชทเข้าใหม่แบบ Live (เทียบ latestMessageId กับรอบก่อนหน้า)
      const latestId = data.latestMessageId || 0;
      const incoming = data.latestIncomingMessage;
      if (isInitial || lastNotifiedMessageId === null) {
        lastNotifiedMessageId = latestId;
      } else if (latestId > lastNotifiedMessageId && incoming) {
        lastNotifiedMessageId = latestId;
        if (activeView === "messages" && activeConversationId === incoming.conversationId) {
          // หากผู้ใช้กำลังเปิดห้องแชทนี้อยู่แล้ว ให้ดึงข้อความล่าสุดมาต่อท้ายโดยรักษาตำแหน่ง Scroll
          await openConversation(incoming.conversationId, { preserveScroll: true });
        } else {
          // หากอยู่หน้าอื่น ให้เด้ง Toast แจ้งเตือนที่คลิกเพื่อกระโดดเข้าห้องแชทได้ทันที
          const snippet = incoming.body.length > 60 ? `${incoming.body.slice(0, 60)}…` : incoming.body;
          showToast(`💬 ข้อความใหม่จาก ${incoming.senderName}: ${snippet}`, false, async () => {
            activeConversationId = incoming.conversationId;
            await switchView("messages");
          });
          if (activeView === "messages") {
            // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
            // Endpoint: GET /api/chat/conversations -> ChatController.GetConversations
            // ข้อมูลที่ส่ง: Auth Cookie อัตโนมัติ
            // ผลลัพธ์จากหลังบ้าน: JSON Array รายการห้องแชทล่าสุด -> อัปเดต `conversationsCache` และเรนเดอร์แถบรายชื่อแชทด้านซ้ายใหม่
            conversationsCache = await api("/api/chat/conversations");
            renderConversationList();
          }
        }
      }
    } catch {
      // ข้ามข้อผิดพลาดชั่วคราวของเครือข่ายระหว่าง Polling เพื่อไม่รบกวนผู้ใช้
    }
  }

  // ==================== 8. หน้าร้านประมูลสาธารณะและรายละเอียดสินค้า (PUBLIC / BUYER: TODAY) ====================
  /**
   * โหลดรายการสินค้าประมูลที่เปิดอยู่ตามเงื่อนไขตัวกรอง (หมวดหมู่, คำค้นหา, การเรียงลำดับ)
   * แล้วเรนเดอร์แถบปุ่มหมวดหมู่ (Category Chips), ข้อความโปรโมชัน และตารางการ์ดสินค้าในหน้า "ประมูลวันนี้"
   */
  async function loadAuctions() {
    const params = new URLSearchParams();
    if (activeCategoryId > 0) params.set("categoryId", String(activeCategoryId));
    if (searchTerm) params.set("search", searchTerm);
    params.set("sort", sortMode);

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
    // Endpoint: GET /api/auctions?categoryId=...&search=...&sort=... -> AuctionsController.GetAuctions
    // ข้อมูลที่ส่ง: Query String `categoryId` (ถ้า > 0), `search` (คำค้นหา), `sort` (โหมดเรียงลำดับ)
    // ผลลัพธ์จากหลังบ้าน: JSON `{ settings, categories, products, totalLiveCount }`
    // -> อัปเดต `globalSettings`, `cachedCategories`, `liveProducts` และเรนเดอร์แถบหมวดหมู่ + การ์ดสินค้า `#productGrid`
    const data = await api(`/api/auctions?${params.toString()}`);
    globalSettings = data.settings;
    cachedCategories = data.categories;
    liveProducts = data.products;

    document.getElementById("heroPromoText").textContent =
      `ชิ้นงานหายากจากนักสะสมทั่วไทย นัดรับได้จริง รับส่วนลด ${globalSettings.sameCategoryDiscountPercent}% อัตโนมัติเมื่อชนะสินค้าหมวดเดียวกัน`;
    document.getElementById("discountHintText").textContent =
      `กติกาปัจจุบัน: ซื้อสินค้าหมวดเดียวกันรับส่วนลด ${globalSettings.sameCategoryDiscountPercent}% · เสนอราคาเพิ่มขั้นต่ำ ${money(globalSettings.globalMinBidIncrement)}`;

    // เรนเดอร์ชิปเลือกหมวดหมู่สินค้า (พร้อมไฮไลต์หมวดที่ผู้ซื้อมีสิทธิ์รับส่วนลดหมวดเดียวกัน)
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

    // เรนเดอร์การ์ดสินค้าประมูลลงใน #productGrid (รองรับทั้งโหมด Grid และ List View)
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

  /**
   * เปิด Modal (`auctionDialog`) แสดงรายละเอียดสินค้า ประวัติการเสนอราคา ปุ่มแชทกับผู้ขาย
   * และฟอร์มเสนอราคา (`#bidForm`) โดยตรวจสอบเงื่อนไขสิทธิ์การบิด (เวลาปิด, บทบาท, เจ้าของสินค้า)
   */
  async function openProduct(productId) {
    try {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: GET /api/auctions/{productId} -> AuctionsController.GetAuctionDetail
      // ข้อมูลที่ส่ง: Route Parameter `productId` (+ Auth Cookie เพื่อเช็กสิทธิ์ส่วนลดและสถานะสินค้า)
      // ผลลัพธ์จากหลังบ้าน: JSON รายละเอียดสินค้า, ราคาสูงสุด, ขั้นต่ำรอบถัดไป (`minNextBid`) และ `history` ประวัติบิด -> เรนเดอร์ใน `#dialogContent`
      const p = await api(`/api/auctions/${productId}`);
      selectedProductId = p.id;

      const historyHtml = p.history.length
        ? p.history.map(b => `
          <div class="dialog-history-row">
            <span>${escapeHtml(b.bidderName)} · ${formatDateTime(b.createdAt)}</span>
            <strong>${money(b.amount)}</strong>
          </div>`).join("")
        : `<p class="dialog-minimum">ยังไม่มีผู้เสนอราคา เป็นคนแรกที่เริ่มประมูลได้เลย</p>`;

      // กำหนดส่วนควบคุมการเสนอราคาให้เหมาะกับสถานะสินค้าและบทบาทของผู้ใช้ปัจจุบัน
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

  // ==================== 9. แดชบอร์ดฝั่งผู้ประมูล / ผู้ซื้อ (BUYER DASHBOARD) ====================
  /**
   * โหลดข้อมูลหน้าผู้ซื้อทั้งหมดในครั้งเดียว:
   * 1) สินค้าที่กดถูกใจ/สนใจ (`savedGrid`)
   * 2) ประวัติการเสนอราคา (`bidHistoryRows`)
   * 3) รายการค้างชำระ (`paymentList`) และประวัติออเดอร์ที่ชนะประมูลทั้งหมดพร้อมปุ่มชำระเงิน/ยืนยันรับของ (`buyerOrdersRows`)
   */
  async function loadBuyerDashboard() {
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
    // Endpoint: GET /api/buyer/dashboard -> AuctionsController.GetBuyerDashboard
    // ข้อมูลที่ส่ง: Auth Cookie อัตโนมัติ (ต้องล็อกอินเป็น Buyer หรือ Seller)
    // ผลลัพธ์จากหลังบ้าน: JSON `{ saved, bidHistory, orders, paymentConfig }`
    // -> อัปเดต `buyerOrdersCache`, `buyerPaymentConfig` และเรนเดอร์ 4 ส่วนของหน้า `mybids` และ `payments`
    const data = await api("/api/buyer/dashboard");

    // 1. เรนเดอร์รายการสินค้าที่ผู้ซื้อบันทึกไว้ (Saved / Watchlist)
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

    // 2. เรนเดอร์ตารางประวัติการเสนอราคาทั้งหมดของผู้ใช้
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

    // แคชรายการออเดอร์และการตั้งค่าบัญชีกลางไว้ใช้ตอนเปิด Modal ชำระเงิน (`openCheckoutModal`)
    buyerOrdersCache = data.orders || [];
    if (data.paymentConfig) buyerPaymentConfig = data.paymentConfig;

    // 3. เรนเดอร์การ์ดรายการค้างชำระ (เฉพาะออเดอร์ที่ paymentStatus === "Unpaid")
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

    // 4. เรนเดอร์ตารางออเดอร์ที่ชนะประมูลทั้งหมด พร้อมสถานะชำระเงิน นัดรับ และปุ่มดำเนินการ
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

  /**
   * แสดงสถานะหน้า "สมัครเป็นผู้ขาย" สำหรับผู้ซื้อ (Buyer)
   * - หากสถานะ `Pending`: แสดงแบนเนอร์รอแอดมินตรวจสอบและซ่อนฟอร์มสมัคร
   * - หากสถานะ `Rejected`: แสดงเหตุผลที่ถูกปฏิเสธและเปิดฟอร์มให้ส่งสมัครใหม่ได้
   */
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

  // ==================== 10. แดชบอร์ดฝั่งผู้ขาย (SELLER DASHBOARD) ====================
  /**
   * โหลดข้อมูลหน้าผู้ขายทั้งหมด:
   * - สถานะอนุมัติร้านค้า (Approved / Pending / Rejected) เพื่อเปิด/ปิดฟอร์มลงขายหรือฟอร์มสมัครใหม่
   * - ตัวเลือกหมวดหมู่และขั้นต่ำการบิดในฟอร์มลงขายสินค้า
   * - ตารางสินค้าที่ผู้ขายลงขาย (`sellerRows`)
   * - สถิติยอดขาย/ค่าธรรมเนียม/ยอดรอโอน (`sellerFinanceStats`) และตารางออเดอร์นัดรับ (`sellerOrdersRows`)
   */
  async function loadSellerDashboard() {
    if (cachedCategories.length === 0) {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: GET /api/auctions -> AuctionsController.GetAuctions
      // ข้อมูลที่ส่ง: ไม่มี (ดึงเฉพาะรายการหมวดหมู่กรณีผู้ขายเข้ามาหน้าแดชบอร์ดโดยตรงและยังไม่มีแคชหมวดหมู่)
      // ผลลัพธ์จากหลังบ้าน: JSON `{ categories, ... }` -> เก็บลง `cachedCategories` เพื่อเติมตัวเลือกใน `<select>`
      const pub = await api("/api/auctions");
      cachedCategories = pub.categories;
    }

    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
    // Endpoint: GET /api/seller/dashboard -> SellerController.GetDashboard
    // ข้อมูลที่ส่ง: Auth Cookie อัตโนมัติ (ต้องล็อกอินเป็น Seller)
    // ผลลัพธ์จากหลังบ้าน: JSON `{ seller, listings, orders, summary, globalMinBidIncrement, platformFeePercent }`
    // -> อัปเดต `sellerListingsCache`, `currentUser` และเรนเดอร์ UI หน้าลงขาย + หน้านัดรับ & ยอดขาย
    const data = await api("/api/seller/dashboard");
    sellerListingsCache = data.listings;
    const seller = data.seller;
    currentUser = seller;

    // เติมรายการหมวดหมู่ในฟอร์มลงขายและตั้งค่าขั้นต่ำการบิดตามกติกาของฝ่ายการเงิน
    const catSelect = document.getElementById("listingCategorySelect");
    catSelect.innerHTML = `<option value="">เลือกหมวดหมู่</option>` +
      cachedCategories.map(c => `<option value="${c.id}">${escapeHtml(c.name)}</option>`).join("");
    const minIncInput = document.getElementById("listingMinIncrement");
    minIncInput.min = data.globalMinBidIncrement;
    if (Number(minIncInput.value) < data.globalMinBidIncrement) minIncInput.value = data.globalMinBidIncrement;
    document.getElementById("sellerMinBidHint").textContent = `ขั้นต่ำตามกติกาฝ่ายการเงิน ≥ ${money(data.globalMinBidIncrement)} · ค่าธรรมเนียมระบบ ${data.platformFeePercent}%`;

    // แสดงแบนเนอร์สถานะการอนุมัติผู้ขาย และควบคุมการมองเห็นของฟอร์มลงขาย / ฟอร์มส่งใบสมัครใหม่
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

    // เรนเดอร์ตารางรายการสินค้าที่ผู้ขายลงประมูลไว้
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

    // เรนเดอร์การ์ดสรุปตัวเลขการเงินและตารางออเดอร์ที่ขายได้ (นัดรับ/ส่งมอบ/ยืนยันรับเงินตรง)
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

  // ==================== 11. ระบบกล่องข้อความและแชท (CHAT: BUYER & SELLER) ====================
  /**
   * โหลดรายชื่อห้องแชททั้งหมดของผู้ใช้ แล้วเปิดห้องเป้าหมาย (หรือห้องแรกสุด) โดยอัตโนมัติ
   */
  async function loadConversations(selectConvId = null) {
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
    // Endpoint: GET /api/chat/conversations -> ChatController.GetConversations
    // ข้อมูลที่ส่ง: Auth Cookie อัตโนมัติ
    // ผลลัพธ์จากหลังบ้าน: JSON Array ของห้องสนทนาพร้อมข้อความล่าสุดและจำนวนที่ยังไม่อ่าน -> เก็บลง `conversationsCache` และเรนเดอร์
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

  /**
   * กรองและเรนเดอร์รายชื่อห้องแชทในแถบด้านซ้าย (`#conversationItems`) ตามคำค้นหาใน `#convSearchInput`
   */
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

  /**
   * เปิดอ่านข้อความในห้องแชทที่เลือก (`convId`), เคลียร์สถานะยังไม่อ่าน, แสดงแบนเนอร์ข้อพิพาท (ถ้ามี),
   * เรนเดอร์ฟองข้อความ (Chat Bubbles) และเลื่อนหน้าต่างข้อความลงล่างสุด (หรือคงตำแหน่งเดิมถ้าเลื่อนอ่านด้านบนอยู่)
   */
  async function openConversation(convId, { preserveScroll = false } = {}) {
    const isSameConv = activeConversationId === convId;
    const msgBox = document.getElementById("chatMessages");
    const prevScrollTop = msgBox ? msgBox.scrollTop : 0;
    // ตรวจว่าผู้ใช้เลื่อนอยู่ใกล้ล่างสุดหรือไม่ (< 90px) เพื่อตัดสินใจว่าจะ auto-scroll ลงล่างไหมตอนมีข้อความใหม่
    const wasNearBottom = !msgBox || (msgBox.scrollHeight - msgBox.scrollTop - msgBox.clientHeight < 90);

    activeConversationId = convId;
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
    // Endpoint: GET /api/chat/conversations/{convId}/messages -> ChatController.GetMessages
    // ข้อมูลที่ส่ง: Route Parameter `convId` (+ Auth Cookie) โดยฝั่งหลังบ้านจะทำเครื่องหมายว่าอ่านข้อความในห้องนี้แล้วอัตโนมัติ
    // ผลลัพธ์จากหลังบ้าน: JSON `{ id, peerName, itemName, auctionItemId, messages, disputes }` -> เรนเดอร์หัวห้องและข้อความใน `#chatMessages`
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

  // ==================== 12. แดชบอร์ดฝั่งผู้ดูแลระบบ (ADMIN DASHBOARD) ====================
  /**
   * โหลดข้อมูลและเรนเดอร์ทุกแท็บของฝั่งผู้ดูแลระบบ (Admin):
   * - การ์ดสถิติรวม (`adminStatsCards`)
   * - ตารางใบสมัครผู้ขายรออนุมัติ (`adminSellerRows`) และสินค้ารออนุมัติ (`adminAuctionRows`)
   * - ตารางจัดการบัญชีผู้ใช้ (`adminUsersRows`) และหมวดหมู่สินค้า (`adminCategoriesRows`)
   * - ตารางเคสข้อพิพาท (`adminDisputesRows`) และประวัติผู้ชนะแบบอ่านอย่างเดียว (`adminWinnersRows`)
   */
  async function loadAdminDashboard() {
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
    // Endpoint: GET /api/admin/dashboard -> AdminController.GetDashboard
    // ข้อมูลที่ส่ง: Auth Cookie อัตโนมัติ (ต้องมีสิทธิ์ Role = Admin)
    // ผลลัพธ์จากหลังบ้าน: JSON `{ stats, pendingSellers, pendingAuctions, users, categories, disputes, winnersReadOnly }`
    // -> เรนเดอร์ลงการ์ดสถิติและตารางทั้ง 6 ส่วนในกลุ่มหน้า `admin-*`
    const data = await api("/api/admin/dashboard");
    const st = data.stats;

    document.getElementById("adminStatsCards").innerHTML = `
      <div><small>ผู้ขายรออนุมัติ</small><strong>${st.pendingSellersCount}</strong><span>ใบสมัครใหม่</span></div>
      <div><small>สินค้ารออนุมัติ</small><strong>${st.pendingAuctionsCount}</strong><span>รอตรวจสอบ</span></div>
      <div><small>กำลังเปิดประมูล</small><strong>${st.liveAuctionsCount}</strong><span>รายการที่แสดงอยู่</span></div>
      <div><small>ข้อพิพาทที่เปิดอยู่</small><strong>${st.openDisputesCount}</strong><span>เคสรอตรวจสอบ</span></div>
    `;

    // 1. ตารางใบสมัครผู้ขายรออนุมัติ
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

    // 2. ตารางสินค้าประมูลรออนุมัติ
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

    // 3. ตารางจัดการบัญชีผู้ใช้ทั้งหมด (ระงับ / ปลดระงับ)
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

    // 4. ตารางจัดการหมวดหมู่สินค้า (เพิ่ม / แก้ไข / ลบหรือปิดใช้งาน)
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

    // 5. ตารางเรื่องร้องเรียนข้อพิพาท
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

    // 6. ตารางประวัติผู้ชนะประมูลแบบอ่านอย่างเดียว (Admin ไม่มีสิทธิ์ปล่อยเงิน/คืนเงิน แยกอำนาจกับฝ่ายการเงิน)
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

  // ==================== 13. แดชบอร์ดฝั่งฝ่ายการเงิน (FINANCE DASHBOARD) ====================
  /**
   * โหลดข้อมูลและเรนเดอร์ทุกหน้าของฝ่ายการเงิน (Finance):
   * - การ์ดสรุปยอดเงิน (`financeSummaryCards`) และตารางตรวจสลิป/ปล่อยเงิน/คืนเงิน (`financeOrdersRows`)
   * - ฟอร์มตั้งกติกาส่วนลด ค่าธรรมเนียม ขั้นต่ำการบิด และบัญชีกลาง (`financeRulesForm`)
   * - การ์ดและตารางรายงานยอดขายสุทธิ (`financeReportCards`, `financeReportRows`)
   */
  async function loadFinanceDashboard() {
    // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
    // Endpoint: GET /api/finance/dashboard -> FinanceController.GetDashboard
    // ข้อมูลที่ส่ง: Auth Cookie อัตโนมัติ (ต้องมีสิทธิ์ Role = Finance)
    // ผลลัพธ์จากหลังบ้าน: JSON `{ summary, settings, orders }` -> เรนเดอร์ลงหน้า `finance-payments`, `finance-rules`, `finance-report`
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

    // เติมค่ากติการะบบปัจจุบันลงในฟอร์มตั้งค่าของฝ่ายการเงิน
    const rf = document.getElementById("financeRulesForm");
    rf.sameCategoryDiscountPercent.value = set.sameCategoryDiscountPercent;
    rf.platformFeePercent.value = set.platformFeePercent;
    rf.globalMinBidIncrement.value = set.globalMinBidIncrement;
    if (rf.platformPromptPayId) rf.platformPromptPayId.value = set.platformPromptPayId || "";
    if (rf.platformAccountName) rf.platformAccountName.value = set.platformAccountName || "";
    document.getElementById("financeRulesUpdated").textContent = `อัปเดตล่าสุด: ${formatDateTime(set.updatedAt)}`;

    // การ์ดสรุปและตารางรายงานยอดขาย/ผู้ชนะ (สามารถกดพิมพ์หรือ Export CSV ได้)
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

  // ==================== 14. MODAL ดำเนินการและตรวจสอบข้อพิพาท (ACTION MODALS) ====================
  /**
   * เปิด Modal อเนกประสงค์สำหรับกรอกเหตุผล/หมายเหตุ (ใช้ซ้ำร่วมกันหลายคำสั่ง เช่น ปฏิเสธผู้ขาย, ปฏิเสธสินค้า,
   * ระงับบัญชี, ปฏิเสธสลิป, ปล่อยเงิน และคืนเงิน) โดยรับ callback `onSubmit(reason)` เพื่อยิง API ของแต่ละคำสั่ง
   */
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

  /**
   * เปิด Modal ให้แอดมินตรวจสอบหลักฐานแชทเฉพาะเคสข้อพิพาทที่ถูกร้องเรียน (`disputeId`)
   * พร้อมฟอร์มอัปเดตสถานะข้อพิพาท (`Open` / `Investigating` / `Resolved`) และบันทึกข้อสรุปของแอดมิน
   */
  async function openDisputeInspector(disputeId) {
    try {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: GET /api/admin/disputes/{disputeId}/messages -> AdminController.GetDisputeChat
      // ข้อมูลที่ส่ง: Route Parameter `disputeId` (+ Auth Cookie สิทธิ์ Admin)
      // ผลลัพธ์จากหลังบ้าน: JSON รายละเอียดข้อพิพาทและรายการข้อความแชทเฉพาะห้องที่เกิดข้อพิพาท -> แสดงใน `#actionDialogContent`
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
          // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
          // Endpoint: POST /api/admin/disputes/{disputeId}/update -> AdminController.UpdateDispute
          // ข้อมูลที่ส่ง: JSON `{ status, adminNote }`
          // ผลลัพธ์จากหลังบ้าน: JSON `{ message }` บันทึกผลข้อพิพาท -> แสดง Toast, ปิด Modal และรีโหลดแดชบอร์ด Admin
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

  // ==================== 15. ตัวดักจับเหตุการณ์และฟอร์มหลักทั้งหมด (GLOBAL EVENT LISTENERS & FORMS) ====================
  // เปิด/ปิดแถบเมนูด้านข้างบนมือถือ, ปุ่มเข้าสู่ระบบบน Hero และปุ่มพิมพ์รายงานการเงิน
  document.getElementById("mobileMenuBtn").addEventListener("click", () => {
    document.getElementById("sidebar").classList.toggle("mobile-open");
    document.getElementById("sidebarBackdrop").classList.toggle("hidden");
  });
  document.getElementById("sidebarBackdrop").addEventListener("click", closeMobileDrawer);
  document.getElementById("heroAuthBtn")?.addEventListener("click", () => openAuthModal("login"));
  document.getElementById("printReportBtn")?.addEventListener("click", () => window.print());

  // ปุ่มสลับมุมมองการ์ดสินค้าระหว่าง Grid View และ List View
  document.getElementById("viewToggleBtn")?.addEventListener("click", () => {
    listViewMode = !listViewMode;
    document.getElementById("productGrid").classList.toggle("list-view", listViewMode);
  });

  // ปุ่มเรียงลำดับสินค้า (ใกล้ปิดประมูล, มาใหม่, ราคาต่ำ-สูง ฯลฯ) แล้วโหลดรายการจากหลังบ้านใหม่
  document.querySelectorAll(".sort-button[data-sort]").forEach(btn => {
    btn.addEventListener("click", () => {
      sortMode = btn.dataset.sort;
      document.querySelectorAll(".sort-button[data-sort]").forEach(b => b.classList.toggle("active", b === btn));
      loadAuctions().catch(e => showToast(e.message, true));
    });
  });

  // ช่องค้นหาสินค้าแบบ Debounce 220 มิลลิวินาที เพื่อไม่ให้ยิง API ถี่เกินไประหว่างผู้ใช้กำลังพิมพ์
  let searchTimer = null;
  document.getElementById("searchInput").addEventListener("input", e => {
    searchTerm = e.target.value.trim();
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => loadAuctions().catch(err => showToast(err.message, true)), 220);
  });

  // ช่องค้นหาห้องแชท (กรองฝั่ง Client จาก conversationsCache ทันที)
  document.getElementById("convSearchInput")?.addEventListener("input", renderConversationList);

  // แท็บสลับโหมดใน Auth Modal และสลับฟิลด์ข้อมูลร้านค้าเมื่อเลือกสมัครบทบาท "Seller"
  document.querySelectorAll(".auth-tab").forEach(tab => {
    tab.addEventListener("click", () => openAuthModal(tab.dataset.authTab));
  });
  document.getElementById("regRoleSelect")?.addEventListener("change", e => {
    const isSeller = e.target.value === "Seller";
    document.getElementById("regSellerFields").classList.toggle("hidden", !isSeller);
    document.querySelectorAll("#regSellerFields input[name='shopName'], #regSellerFields input[name='phone'], #regSellerFields input[name='bankAccount']")
      .forEach(inp => { inp.required = isSeller; });
  });

  // ฟอร์มเข้าสู่ระบบ (Login Form)
  document.getElementById("loginForm").addEventListener("submit", async e => {
    e.preventDefault();
    const form = e.currentTarget;
    const fd = new FormData(form);
    try {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: POST /api/auth/login -> AuthController.Login
      // ข้อมูลที่ส่ง: JSON `{ email, password }`
      // ผลลัพธ์จากหลังบ้าน: ตั้งค่า Auth Cookie (Set-Cookie) + คืน JSON `{ message }` -> ปิด Modal, แสดง Toast และรีเฟรช Session เข้าหน้าแรกตามบทบาท
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

  // ฟอร์มสมัครสมาชิกใหม่ (Register Form: Buyer หรือ Seller)
  document.getElementById("registerForm").addEventListener("submit", async e => {
    e.preventDefault();
    const form = e.currentTarget;
    const fd = new FormData(form);
    try {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: POST /api/auth/register -> AuthController.Register
      // ข้อมูลที่ส่ง: JSON `{ displayName, email, password, role, shopName, phone, bankAccount, sellerBio }`
      // ผลลัพธ์จากหลังบ้าน: สร้างบัญชี, ออก Auth Cookie และคืน JSON `{ message }` -> รีเซ็ตฟอร์ม, ปิด Modal และรีเฟรช Session
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

  // ปุ่มกระดิ่งแจ้งเตือน (เปิด/ปิด Dropdown) และปุ่ม "อ่านทั้งหมดแล้ว"
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
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: POST /api/notifications/read-all -> NotificationsController.MarkAllRead
      // ข้อมูลที่ส่ง: Auth Cookie อัตโนมัติ (ไม่มี body)
      // ผลลัพธ์จากหลังบ้าน: อัปเดตสถานะการแจ้งเตือนและข้อความแชททั้งหมดเป็นอ่านแล้ว -> เคลียร์ unreadCount ในแคชและอัปเดต Badge
      await api("/api/notifications/read-all", { method: "POST" });
      conversationsCache.forEach(c => { c.unreadCount = 0; });
      if (activeView === "messages") renderConversationList();
      await pollNotifications(true);
      showToast("ทำเครื่องหมายว่าอ่านทั้งหมดแล้ว");
    } catch (err) { showToast(err.message, true); }
  });

  // ฟอร์มยืนยันเสนอราคา (`#bidForm`) ที่ถูกสร้างแบบไดนามิกภายใน `auctionDialog` (ใช้ Event Delegation บน dialog)
  auctionDialog.addEventListener("submit", async e => {
    if (e.target.id !== "bidForm") return;
    e.preventDefault();
    const form = e.target;
    // ตรวจสอบเวลาปิดประมูลฝั่งหน้าบ้านทันทีเพื่อป้องกันการกดบิดหลังหมดเวลา
    if (form.dataset.endAt && parseUtcDate(form.dataset.endAt).getTime() <= Date.now()) {
      showToast("รายการนี้ปิดประมูลแล้ว ไม่สามารถเสนอราคาได้", true);
      await openProduct(selectedProductId);
      if (activeView === "today") await loadAuctions();
      return;
    }
    const amount = Number(new FormData(form).get("bid"));
    try {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: POST /api/auctions/{selectedProductId}/bid -> AuctionsController.PlaceBid
      // ข้อมูลที่ส่ง: JSON `{ amount }` (จำนวนเงินที่เสนอ) + Route Parameter `selectedProductId`
      // ผลลัพธ์จากหลังบ้าน: บันทึกการเสนอราคา (และต่อเวลาอัตโนมัติ 2 นาทีหากใกล้หมดเวลา) คืน JSON `{ message }`
      // -> โหลดรายละเอียดสินค้าใหม่เพื่ออัปเดตราคาสูงสุด/ประวัติบิด และรีเฟรชหน้าร้านหรือหน้าประวัติบิด
      const res = await api(`/api/auctions/${selectedProductId}/bid`, {
        method: "POST",
        body: JSON.stringify({ amount })
      });
      showToast(res.message);
      await openProduct(selectedProductId);
      if (activeView === "today") await loadAuctions();
      else if (activeView === "mybids") await loadBuyerDashboard();
    } catch (err) {
      // กรณีมีคนบิดตัดหน้าหรือราคาต่ำกว่าเกณฑ์ ให้แจ้ง Error และรีเฟรชราคาสูงสุดล่าสุดมาแสดงทันที
      showToast(err.message, true);
      await openProduct(selectedProductId);
      if (activeView === "today") await loadAuctions();
    }
  });

  // ฝั่งผู้ขาย: อัปโหลดไฟล์รูปภาพสินค้าทันทีที่เลือกไฟล์ เพื่อรับ URL กลับมากรอกในช่อง imageUrl อัตโนมัติ
  document.getElementById("listingImageFile")?.addEventListener("change", async e => {
    const file = e.currentTarget.files?.[0];
    if (!file) return;
    const fd = new FormData();
    fd.append("file", file);
    try {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: POST /api/seller/upload-image -> SellerController.UploadProductImage
      // ข้อมูลที่ส่ง: multipart/form-data (`FormData` ฟิลด์ `file`)
      // ผลลัพธ์จากหลังบ้าน: JSON `{ imageUrl, message }` (บันทึกไฟล์ลง wwwroot/uploads) -> เติม URL และแสดงรูปตัวอย่าง Preview
      const res = await api("/api/seller/upload-image", { method: "POST", body: fd });
      document.getElementById("listingImageUrlInput").value = res.imageUrl;
      document.getElementById("listingImagePreview").src = res.imageUrl;
      document.getElementById("listingImagePreviewWrap").classList.remove("hidden");
      showToast(res.message);
    } catch (err) {
      showToast(err.message, true);
    }
  });

  // ฝั่งผู้ขาย: ฟอร์มลงขายสินค้าประมูลชิ้นใหม่
  document.getElementById("listingForm")?.addEventListener("submit", async e => {
    e.preventDefault();
    const form = e.currentTarget;
    const fd = new FormData(form);
    try {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: POST /api/seller/listings -> SellerController.CreateListing
      // ข้อมูลที่ส่ง: JSON `{ name, categoryId, condition, description, imageUrl, startPrice, minIncrement, startAt (UTC ISO), endAt (UTC ISO) }`
      // ผลลัพธ์จากหลังบ้าน: สร้างสินค้าสถานะ Pending รอแอดมินอนุมัติ คืน JSON `{ message }` -> รีเซ็ตฟอร์มและรีโหลดแดชบอร์ดผู้ขาย
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

  // ฟอร์มลืมรหัสผ่าน (ขอรหัสรีเซ็ต 6 หลัก) และฟอร์มตั้งรหัสผ่านใหม่
  document.getElementById("forgotPasswordForm")?.addEventListener("submit", async e => {
    e.preventDefault();
    const form = e.currentTarget;
    const email = String(new FormData(form).get("email") || "").trim();
    try {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: POST /api/auth/forgot-password -> AuthController.ForgotPassword
      // ข้อมูลที่ส่ง: JSON `{ email }`
      // ผลลัพธ์จากหลังบ้าน: JSON `{ message, devResetCode? }` (ในโหมด Development จะส่งรหัส 6 หลักกลับมาแสดงช่วยทดสอบ)
      // -> กรอกอีเมลและรหัสลงในฟอร์ม `#resetPasswordForm` ให้อัตโนมัติ
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
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: POST /api/auth/reset-password -> AuthController.ResetPassword
      // ข้อมูลที่ส่ง: JSON `{ email, resetToken, newPassword }`
      // ผลลัพธ์จากหลังบ้าน: ตรวจรหัสรีเซ็ตและอัปเดตรหัสผ่านใหม่ คืน JSON `{ message }` -> ล้างฟอร์มและสลับกลับไปแท็บเข้าสู่ระบบ
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

  // ฝั่งผู้ขาย (ที่เคยถูกปฏิเสธ): ฟอร์มแก้ไขข้อมูลและส่งใบสมัครผู้ขายใหม่อีกครั้ง
  document.getElementById("sellerReapplyForm")?.addEventListener("submit", async e => {
    e.preventDefault();
    const fd = new FormData(e.currentTarget);
    try {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: POST /api/seller/apply -> SellerController.UpdateApplication
      // ข้อมูลที่ส่ง: JSON `{ shopName, phone, bankAccount, sellerBio }`
      // ผลลัพธ์จากหลังบ้าน: เปลี่ยนสถานะใบสมัครเป็น Pending รอแอดมินตรวจใหม่ คืน JSON `{ message }` -> รีโหลดแดชบอร์ดผู้ขาย
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

  // ฝั่งผู้ซื้อ (Buyer): ฟอร์มยื่นคำขออัปเกรดบัญชีเป็นผู้ขาย (ใช้ Endpoint เดียวกับผู้ขายที่สมัครใหม่)
  document.getElementById("buyerApplySellerForm")?.addEventListener("submit", async e => {
    e.preventDefault();
    const fd = new FormData(e.currentTarget);
    try {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: POST /api/seller/apply -> SellerController.UpdateApplication
      // ข้อมูลที่ส่ง: JSON `{ shopName, phone, bankAccount, sellerBio }`
      // ผลลัพธ์จากหลังบ้าน: JSON `{ message, user }` อัปเดตข้อมูลใบสมัครในผู้ใช้ -> อัปเดต `currentUser` และแสดงแบนเนอร์รออนุมัติ
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

  // ฟอร์มส่งข้อความในห้องแชทปัจจุบัน
  document.getElementById("chatForm")?.addEventListener("submit", async e => {
    e.preventDefault();
    if (!activeConversationId) return;
    const input = document.getElementById("chatInput");
    const body = input.value.trim();
    if (!body) return;
    try {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: POST /api/chat/conversations/{activeConversationId}/messages -> ChatController.SendMessage
      // ข้อมูลที่ส่ง: JSON `{ body }` (ข้อความแชท) + Route Parameter `activeConversationId`
      // ผลลัพธ์จากหลังบ้าน: บันทึกข้อความลงฐานข้อมูล -> ล้างช่องพิมพ์, โหลดห้องแชทใหม่ และโฟกัสกลับที่ช่องพิมพ์
      await api(`/api/chat/conversations/${activeConversationId}/messages`, {
        method: "POST",
        body: JSON.stringify({ body })
      });
      input.value = "";
      await loadConversations(activeConversationId);
      input.focus();
    } catch (err) { showToast(err.message, true); }
  });

  // ฝั่งแอดมิน: ฟอร์มเพิ่มหมวดหมู่สินค้าใหม่ หรือแก้ไขหมวดหมู่เดิม (ดูจากว่ามีค่า hidden field `id` หรือไม่)
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
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint (โหมดแก้ไข): PUT /api/admin/categories/{id} -> AdminController.UpdateCategory
      // Endpoint (โหมดเพิ่มใหม่): POST /api/admin/categories -> AdminController.CreateCategory
      // ข้อมูลที่ส่ง: JSON `{ name, symbol, description, isActive }`
      // ผลลัพธ์จากหลังบ้าน: JSON `{ message }` -> รีเซ็ตฟอร์มกลับเป็นโหมดเพิ่มใหม่และรีโหลดแดชบอร์ดแอดมิน
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

  // ปุ่มยกเลิกการแก้ไขหมวดหมู่ (ล้างค่าฟอร์มกลับสู่โหมดเพิ่มหมวดหมู่ใหม่)
  document.getElementById("catCancelEditBtn")?.addEventListener("click", () => {
    const form = document.getElementById("adminCategoryForm");
    form.reset();
    form.id.value = "";
    form.symbol.value = "✦";
    document.getElementById("catSubmitBtn").textContent = "เพิ่มหมวดหมู่";
    document.getElementById("catCancelEditBtn").classList.add("hidden");
  });

  // ฝั่งฝ่ายการเงิน: ฟอร์มอัปเดตกติกาส่วนลด ค่าธรรมเนียม ขั้นต่ำการบิด และบัญชีกลาง PromptPay
  document.getElementById("financeRulesForm")?.addEventListener("submit", async e => {
    e.preventDefault();
    const fd = new FormData(e.currentTarget);
    try {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: PUT /api/finance/settings -> FinanceController.UpdateSettings
      // ข้อมูลที่ส่ง: JSON `{ sameCategoryDiscountPercent, platformFeePercent, globalMinBidIncrement, platformPromptPayId, platformAccountName }`
      // ผลลัพธ์จากหลังบ้าน: อัปเดตกติกาส่วนกลางในฐานข้อมูลพร้อมบันทึก Audit Log คืน JSON `{ message }` -> รีโหลดแดชบอร์ดการเงิน
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

  // ==================== 16. ตัวดักจับการคลิกแบบ DELEGATION ทั้งหน้าเว็บ (GLOBAL CLICK DELEGATION) ====================
  // ใช้ Event Delegation ผูก listener เพียงตัวเดียวที่ `document` เพื่อรองรับปุ่มทุกตัวที่ถูกเรนเดอร์แบบไดนามิกด้วย `innerHTML`
  // โดยจำแนกคำสั่งจาก `data-*` attribute ของปุ่มที่ถูกคลิก
  document.addEventListener("click", async event => {
    const target = event.target instanceof Element ? event.target : null;
    if (!target) return;

    // คลิกนอกกล่องแจ้งเตือน (#notifWrapper) ให้พับปิด Dropdown แจ้งเตือนอัตโนมัติ
    if (!target.closest("#notifWrapper")) {
      document.getElementById("notifDropdown")?.classList.add("hidden");
    }

    // คลิกที่รายการแจ้งเตือนใน Dropdown -> ทำเครื่องหมายว่าอ่านแล้วและพาไปยังหน้า/ห้องแชท/สินค้าที่เกี่ยวข้อง
    const notifItem = target.closest("[data-notif-view]");
    if (notifItem) {
      const notifId = notifItem.dataset.notifId;
      const targetView = notifItem.dataset.notifView;
      const refId = notifItem.dataset.notifRef ? Number(notifItem.dataset.notifRef) : null;
      document.getElementById("notifDropdown")?.classList.add("hidden");
      if (notifId) {
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
        // Endpoint: POST /api/notifications/{notifId}/read -> NotificationsController.MarkRead
        // ข้อมูลที่ส่ง: Route Parameter `notifId` (+ Auth Cookie)
        // ผลลัพธ์จากหลังบ้าน: ทำเครื่องหมายว่ารายการแจ้งเตือนนี้ถูกอ่านแล้ว (ยิงแบบ fire-and-forget)
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

    // ปุ่มสลับแท็บในหน้าต่าง Auth (รวมลิงก์ "ลืมรหัสผ่าน?" ที่อยู่ในฟอร์ม)
    const authTabBtn = target.closest("[data-auth-tab]");
    if (authTabBtn) {
      await openAuthModal(authTabBtn.dataset.authTab);
      return;
    }

    // ปุ่มกรองบทบาทในหน้า Audit Log (all / Admin / Finance / Seller / Buyer)
    const auditFilterBtn = target.closest("[data-audit-filter]");
    if (auditFilterBtn) {
      document.querySelectorAll("[data-audit-filter]").forEach(b => b.classList.toggle("active", b === auditFilterBtn));
      await loadAuditLogs(auditFilterBtn.dataset.auditFilter).catch(err => showToast(err.message, true));
      return;
    }

    // ปุ่มเปิด Modal เปลี่ยนรหัสผ่าน (สำหรับผู้ใช้ที่ล็อกอินอยู่)
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
          // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
          // Endpoint: POST /api/auth/change-password -> AuthController.ChangePassword
          // ข้อมูลที่ส่ง: JSON `{ currentPassword, newPassword }` (+ Auth Cookie)
          // ผลลัพธ์จากหลังบ้าน: ตรวจรหัสเดิมและอัปเดตรหัสใหม่ คืน JSON `{ message }` -> แสดง Toast และปิด Modal
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

    // ฝั่งผู้ขาย: เปิด Modal แก้ไขสินค้าที่ยังรอตรวจ (Pending) หรือถูกปฏิเสธ (Rejected)
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
          // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
          // Endpoint: POST /api/seller/upload-image -> SellerController.UploadProductImage
          // ข้อมูลที่ส่ง: multipart/form-data (`FormData` ฟิลด์ `file`)
          // ผลลัพธ์จากหลังบ้าน: JSON `{ imageUrl, message }` -> อัปเดตช่อง `#editListingImgUrl` ด้วย URL รูปใหม่
          const res = await api("/api/seller/upload-image", { method: "POST", body: fd });
          document.getElementById("editListingImgUrl").value = res.imageUrl;
          showToast(res.message);
        } catch (err) { showToast(err.message, true); }
      });
      document.getElementById("editListingForm").addEventListener("submit", async e => {
        e.preventDefault();
        const fd = new FormData(e.currentTarget);
        try {
          // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
          // Endpoint: PUT /api/seller/listings/{item.id} -> SellerController.UpdateListing
          // ข้อมูลที่ส่ง: JSON ข้อมูลสินค้าที่แก้ไขครบทุกฟิลด์ + Route Parameter `item.id`
          // ผลลัพธ์จากหลังบ้าน: อัปเดตสินค้าและเปลี่ยนสถานะเป็น Pending ส่งตรวจใหม่ คืน JSON `{ message }` -> ปิด Modal และรีโหลดแดชบอร์ดผู้ขาย
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

    // ฝั่งผู้ขาย: ยกเลิก/ลบรายการสินค้าที่ยังรอตรวจหรือถูกปฏิเสธ
    const delListBtn = target.closest("[data-delete-listing]");
    if (delListBtn) {
      if (!window.confirm(`ยืนยันยกเลิกรายการสินค้า “${delListBtn.dataset.name}” ใช่หรือไม่?`)) return;
      try {
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
        // Endpoint: DELETE /api/seller/listings/{id} -> SellerController.DeleteListing
        // ข้อมูลที่ส่ง: Route Parameter `id` (+ Auth Cookie สิทธิ์เจ้าของสินค้า)
        // ผลลัพธ์จากหลังบ้าน: ลบสินค้าออกจากฐานข้อมูล คืน JSON `{ message }` -> แสดง Toast และรีโหลดแดชบอร์ดผู้ขาย
        const res = await api(`/api/seller/listings/${delListBtn.dataset.deleteListing}`, { method: "DELETE" });
        showToast(res.message);
        await loadSellerDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    // ปุ่มปิด Modal อเนกประสงค์ (`actionDialog`)
    if (target.closest("[data-close-action]")) {
      actionDialog.close();
      return;
    }

    // ปุ่มเมนูเปลี่ยนหน้าจอ (View Link) ทั้งบน Sidebar และปุ่มลิงก์ภายในหน้า
    const viewLink = target.closest("[data-view-link]");
    if (viewLink) {
      event.preventDefault();
      await switchView(viewLink.dataset.viewLink);
      return;
    }

    // ปุ่มเปิดหน้าต่างเข้าสู่ระบบ / สลับบัญชี
    if (target.closest("[data-open-auth]")) {
      await openAuthModal("login");
      return;
    }

    // ปุ่มออกจากระบบ (Logout)
    if (target.closest("#logoutBtn")) {
      // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
      // Endpoint: POST /api/auth/logout -> AuthController.Logout
      // ข้อมูลที่ส่ง: Auth Cookie อัตโนมัติ (ไม่มี body)
      // ผลลัพธ์จากหลังบ้าน: ล้าง Auth Cookie ใน Browser -> แสดง Toast และรีเฟรช Session กลับหน้าสาธารณะ
      await api("/api/auth/logout", { method: "POST" });
      showToast("ออกจากระบบแล้ว");
      await refreshSession(true);
      return;
    }

    // การ์ดคลิกล็อกอินด่วนด้วยบัญชีทดลอง (Demo Account 4 บทบาท ใช้รหัสผ่านมาตรฐาน "123456")
    const demoBtn = target.closest("[data-demo-email]");
    if (demoBtn) {
      try {
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
        // Endpoint: POST /api/auth/login -> AuthController.Login
        // ข้อมูลที่ส่ง: JSON `{ email: demoBtn.dataset.demoEmail, password: "123456" }`
        // ผลลัพธ์จากหลังบ้าน: ออก Auth Cookie ของบัญชีทดลอง คืน JSON `{ message }` -> ปิด Modal และพาเข้าหน้าหลักของบทบาทนั้น
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

    // ชิปเลือกกรองตามหมวดหมู่สินค้าในหน้า "ประมูลวันนี้"
    const catBtn = target.closest("[data-category]");
    if (catBtn) {
      activeCategoryId = Number(catBtn.dataset.category || 0);
      await loadAuctions().catch(e => showToast(e.message, true));
      return;
    }

    // ปุ่มหัวใจ (♡ / ♥) บันทึกหรือนำออกจากรายการที่สนใจ (Watchlist Toggle)
    const saveBtn = target.closest("[data-save]");
    if (saveBtn) {
      try {
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
        // Endpoint: POST /api/auctions/{id}/save -> AuctionsController.ToggleSave
        // ข้อมูลที่ส่ง: Route Parameter `id` (+ Auth Cookie)
        // ผลลัพธ์จากหลังบ้าน: สลับสถานะการบันทึกสินค้า คืน JSON `{ message }` -> รีเฟรชหน้าร้านหรือหน้า "รายการที่สนใจ"
        const res = await api(`/api/auctions/${saveBtn.dataset.save}/save`, { method: "POST" });
        showToast(res.message);
        if (activeView === "today") await loadAuctions();
        else if (activeView === "mybids") await loadBuyerDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    // คลิกที่การ์ดสินค้า / ชื่อสินค้า / ปุ่มดูรายละเอียด เพื่อเปิด Modal รายละเอียดและเสนอราคา
    const detailBtn = target.closest("[data-detail]");
    if (detailBtn) {
      event.preventDefault();
      await openProduct(Number(detailBtn.dataset.detail));
      return;
    }

    // ปุ่มเลื่อนหน้าจอแบบนุ่มนวลไปยังส่วนเป้าหมาย (Smooth Scroll)
    const scrollBtn = target.closest("[data-scroll]");
    if (scrollBtn) {
      document.getElementById(scrollBtn.dataset.scroll)?.scrollIntoView({ behavior: "smooth" });
      return;
    }

    // ปุ่ม "ดูสลิป" เปิด Modal แสดงภาพหลักฐานการโอนเงินขนาดเต็ม
    const slipBtn = target.closest("[data-view-slip]");
    if (slipBtn) {
      openSlipModal(slipBtn.dataset.viewSlip, slipBtn.dataset.slipTitle || "หลักฐานการชำระเงิน", slipBtn.dataset.slipNote || "");
      return;
    }

    // ฝั่งผู้ซื้อ: เปิด Modal ชำระเงินแบบผสม (QR ระบบกลาง Escrow หรือ จ่ายตรงผู้ขาย)
    const payBtn = target.closest("[data-pay-order]");
    if (payBtn) {
      const order = buyerOrdersCache.find(o => o.id === Number(payBtn.dataset.payOrder));
      if (!order) return;
      openCheckoutModal(order);
      return;
    }

    // ฝั่งผู้ขาย: ยืนยันรับเงินกรณีผู้ซื้อเลือกจ่ายตรง (DirectToSeller)
    const sellerVerifyBtn = target.closest("[data-seller-verify-pay]");
    if (sellerVerifyBtn) {
      try {
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
        // Endpoint: POST /api/seller/orders/{orderId}/verify-payment -> SellerController.VerifyDirectPayment
        // ข้อมูลที่ส่ง: JSON `{ approve: true }` + Route Parameter `orderId`
        // ผลลัพธ์จากหลังบ้าน: เปลี่ยนสถานะชำระเงินเป็น Paid และ payout เป็น DirectSettled คืน JSON `{ message }` -> รีโหลดแดชบอร์ดผู้ขาย
        const res = await api(`/api/seller/orders/${sellerVerifyBtn.dataset.sellerVerifyPay}/verify-payment`, {
          method: "POST", body: JSON.stringify({ approve: true })
        });
        showToast(res.message);
        await loadSellerDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    // ฝั่งผู้ขาย: ปฏิเสธการแจ้งชำระเงินตรง (เปิด Modal กรอกเหตุผลแจ้งกลับผู้ซื้อ)
    const sellerRejectPayBtn = target.closest("[data-seller-reject-pay]");
    if (sellerRejectPayBtn) {
      showReasonModal({
        title: `ปฏิเสธการแจ้งชำระเงินตรง (${sellerRejectPayBtn.dataset.code})`,
        subtitle: "ระบบจะแจ้งเหตุผลให้ผู้ซื้อตรวจสอบและส่งใหม่",
        label: "เหตุผล",
        placeholder: "เช่น ยังไม่พบยอดโอนเข้าบัญชี หรือยอดเงินไม่ครบถ้วน",
        submitText: "ยืนยันปฏิเสธ",
        onSubmit: async reason => {
          // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
          // Endpoint: POST /api/seller/orders/{orderId}/verify-payment -> SellerController.VerifyDirectPayment
          // ข้อมูลที่ส่ง: JSON `{ approve: false, reason }`
          // ผลลัพธ์จากหลังบ้าน: รีเซ็ตสถานะออเดอร์กลับเป็น Unpaid พร้อมบันทึกเหตุผลและแจ้งเตือนผู้ซื้อ -> รีโหลดแดชบอร์ดผู้ขาย
          const res = await api(`/api/seller/orders/${sellerRejectPayBtn.dataset.sellerRejectPay}/verify-payment`, {
            method: "POST", body: JSON.stringify({ approve: false, reason })
          });
          showToast(res.message);
          await loadSellerDashboard();
        }
      });
      return;
    }

    // ฝั่งฝ่ายการเงิน: อนุมัติสลิปการโอนเงินเข้าระบบกลาง (Escrow)
    const finVerifyBtn = target.closest("[data-finance-verify-pay]");
    if (finVerifyBtn) {
      try {
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
        // Endpoint: POST /api/finance/orders/{orderId}/verify-payment -> FinanceController.VerifyEscrowPayment
        // ข้อมูลที่ส่ง: JSON `{ approve: true }` + Route Parameter `orderId`
        // ผลลัพธ์จากหลังบ้าน: เปลี่ยนสถานะเป็น Paid และแจ้งผู้ขายให้นัดรับ/ส่งมอบ คืน JSON `{ message }` -> รีโหลดแดชบอร์ดการเงิน
        const res = await api(`/api/finance/orders/${finVerifyBtn.dataset.financeVerifyPay}/verify-payment`, {
          method: "POST", body: JSON.stringify({ approve: true })
        });
        showToast(res.message);
        await loadFinanceDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    // ฝั่งฝ่ายการเงิน: ปฏิเสธสลิปโอนเงินเข้าระบบกลาง (ให้ผู้ซื้ออัปโหลดใหม่)
    const finRejectPayBtn = target.closest("[data-finance-reject-pay]");
    if (finRejectPayBtn) {
      showReasonModal({
        title: `ปฏิเสธสลิปชำระเงิน (${finRejectPayBtn.dataset.code})`,
        subtitle: "ผู้ซื้อจะได้รับแจ้งเตือนและสามารถอัปโหลดสลิปใหม่ได้",
        label: "เหตุผลในการปฏิเสธสลิป",
        placeholder: "เช่น สลิปไม่ชัดเจน หรือยอดโอนไม่ตรงกับยอดสุทธิ",
        submitText: "ยืนยันปฏิเสธสลิป",
        onSubmit: async reason => {
          // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
          // Endpoint: POST /api/finance/orders/{orderId}/verify-payment -> FinanceController.VerifyEscrowPayment
          // ข้อมูลที่ส่ง: JSON `{ approve: false, reason }`
          // ผลลัพธ์จากหลังบ้าน: รีเซ็ตออเดอร์เป็น Unpaid พร้อมเหตุผลและแจ้งเตือนผู้ซื้อ -> รีโหลดแดชบอร์ดการเงิน
          const res = await api(`/api/finance/orders/${finRejectPayBtn.dataset.financeRejectPay}/verify-payment`, {
            method: "POST", body: JSON.stringify({ approve: false, reason })
          });
          showToast(res.message);
          await loadFinanceDashboard();
        }
      });
      return;
    }

    // ฝั่งผู้ซื้อ: กดยืนยันว่าได้รับสินค้าเรียบร้อยแล้ว (ปลดล็อกให้ฝ่ายการเงินปล่อยเงิน Escrow ให้ผู้ขายได้)
    const recBtn = target.closest("[data-confirm-received]");
    if (recBtn) {
      try {
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
        // Endpoint: POST /api/buyer/orders/{orderId}/confirm-received -> AuctionsController.ConfirmReceived
        // ข้อมูลที่ส่ง: Route Parameter `orderId` (+ Auth Cookie สิทธิ์ผู้ซื้อ)
        // ผลลัพธ์จากหลังบ้าน: เปลี่ยนสถานะนัดรับเป็น Completed คืน JSON `{ message }` -> แสดง Toast และรีโหลดแดชบอร์ดผู้ซื้อ
        const res = await api(`/api/buyer/orders/${recBtn.dataset.confirmReceived}/confirm-received`, { method: "POST" });
        showToast(res.message);
        await loadBuyerDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    // แชท: เริ่มสร้างห้องสนทนาใหม่กับผู้ขายจากหน้าสินค้า หรือเปิดห้องแชทที่มีอยู่แล้ว
    const startChatBtn = target.closest("[data-start-chat]");
    if (startChatBtn) {
      try {
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
        // Endpoint: POST /api/chat/start -> ChatController.StartConversation
        // ข้อมูลที่ส่ง: JSON `{ auctionItemId }` (+ Auth Cookie)
        // ผลลัพธ์จากหลังบ้าน: JSON `{ conversationId }` (สร้างห้องใหม่หรือคืนห้องเดิม) -> ปิด Modal สินค้าและสลับไปหน้ากล่องข้อความ
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

    // ปุ่มเปิดห้องแชทโดยระบุ `conversationId` โดยตรง (จากตารางออเดอร์ผู้ซื้อ/ผู้ขาย)
    const openConvBtn = target.closest("[data-open-conv]");
    if (openConvBtn) {
      activeConversationId = Number(openConvBtn.dataset.openConv);
      await switchView("messages");
      return;
    }

    // คลิกเลือกห้องสนทนาจากแถบรายชื่อด้านซ้ายในหน้ากล่องข้อความ
    const selConvBtn = target.closest("[data-select-conv]");
    if (selConvBtn) {
      await openConversation(Number(selConvBtn.dataset.selectConv));
      return;
    }

    // ปุ่ม "แจ้งข้อพิพาท" จากภายในห้องแชท -> เปิด Modal กรอกรายละเอียดร้องเรียนให้แอดมินตรวจสอบ
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
          // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
          // Endpoint: POST /api/chat/conversations/{convId}/dispute -> ChatController.ReportDispute
          // ข้อมูลที่ส่ง: JSON `{ reason, details, orderPaymentId }` + Route Parameter `convId`
          // ผลลัพธ์จากหลังบ้าน: สร้างเคสข้อพิพาท (ล็อกการปล่อยเงินชั่วคราวและเปิดสิทธิ์ให้ Admin อ่านแชทเคสนี้) คืน JSON `{ message }` -> ปิด Modal และรีโหลดห้องแชท
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

    // ฝั่งผู้ขาย: เปิด Modal ดูตารางประวัติการเสนอราคาทั้งหมดของสินค้าตัวเอง (ดึงจาก `sellerListingsCache`)
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

    // ฝั่งผู้ขาย: เปิด Modal กำหนดสถานที่และวันเวลานัดรับสินค้า
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
          // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
          // Endpoint: POST /api/seller/orders/{orderId}/meetup -> SellerController.ScheduleMeetup
          // ข้อมูลที่ส่ง: JSON `{ location, meetupAt (UTC ISO), note }` + Route Parameter `orderId`
          // ผลลัพธ์จากหลังบ้าน: บันทึกนัดรับและส่งข้อความแจ้งเข้าห้องแชทผู้ซื้ออัตโนมัติ คืน JSON `{ message }` -> ปิด Modal และรีโหลดแดชบอร์ดผู้ขาย
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

    // ฝั่งผู้ขาย: กดยืนยันว่าได้ส่งมอบสินค้าให้ผู้ซื้อแล้ว
    const deliverBtn = target.closest("[data-confirm-deliver]");
    if (deliverBtn) {
      try {
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
        // Endpoint: POST /api/seller/orders/{orderId}/deliver -> SellerController.ConfirmDelivered
        // ข้อมูลที่ส่ง: Route Parameter `orderId` (+ Auth Cookie สิทธิ์ผู้ขาย)
        // ผลลัพธ์จากหลังบ้าน: เปลี่ยนสถานะนัดรับเป็น Delivered และแจ้งเตือนผู้ซื้อ คืน JSON `{ message }` -> รีโหลดแดชบอร์ดผู้ขาย
        const res = await api(`/api/seller/orders/${deliverBtn.dataset.confirmDeliver}/deliver`, { method: "POST" });
        showToast(res.message);
        await loadSellerDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    // ฝั่งแอดมิน: อนุมัติใบสมัครผู้ขาย
    const appSellerBtn = target.closest("[data-approve-seller]");
    if (appSellerBtn) {
      try {
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
        // Endpoint: POST /api/admin/sellers/{userId}/review -> AdminController.ReviewSeller
        // ข้อมูลที่ส่ง: JSON `{ approve: true }` + Route Parameter `userId`
        // ผลลัพธ์จากหลังบ้าน: เปลี่ยน Role/สถานะผู้สมัครเป็นผู้ขายที่อนุมัติแล้ว + บันทึก Audit Log คืน JSON `{ message }` -> รีโหลดแดชบอร์ดแอดมิน
        const res = await api(`/api/admin/sellers/${appSellerBtn.dataset.approveSeller}/review`, {
          method: "POST", body: JSON.stringify({ approve: true })
        });
        showToast(res.message);
        await loadAdminDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    // ฝั่งแอดมิน: ปฏิเสธใบสมัครผู้ขาย (พร้อมระบุเหตุผล)
    const rejSellerBtn = target.closest("[data-reject-seller]");
    if (rejSellerBtn) {
      showReasonModal({
        title: `ปฏิเสธใบสมัครผู้ขาย: ${rejSellerBtn.dataset.name}`,
        subtitle: "เหตุผลนี้จะถูกแสดงให้ผู้สมัครทราบเพื่อแก้ไข",
        label: "เหตุผลการปฏิเสธ",
        placeholder: "เช่น ข้อมูลบัญชีรับเงินไม่ชัดเจน",
        submitText: "ยืนยันปฏิเสธ",
        onSubmit: async reason => {
          // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
          // Endpoint: POST /api/admin/sellers/{userId}/review -> AdminController.ReviewSeller
          // ข้อมูลที่ส่ง: JSON `{ approve: false, reason }`
          // ผลลัพธ์จากหลังบ้าน: เปลี่ยนสถานะใบสมัครเป็น Rejected พร้อมเหตุผล + Audit Log -> รีโหลดแดชบอร์ดแอดมิน
          const res = await api(`/api/admin/sellers/${rejSellerBtn.dataset.rejectSeller}/review`, {
            method: "POST", body: JSON.stringify({ approve: false, reason })
          });
          showToast(res.message);
          await loadAdminDashboard();
        }
      });
      return;
    }

    // ฝั่งแอดมิน: อนุมัติสินค้าประมูลขึ้นหน้าร้าน
    const appAucBtn = target.closest("[data-approve-auction]");
    if (appAucBtn) {
      try {
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
        // Endpoint: POST /api/admin/auctions/{itemId}/review -> AdminController.ReviewAuction
        // ข้อมูลที่ส่ง: JSON `{ approve: true }` + Route Parameter `itemId`
        // ผลลัพธ์จากหลังบ้าน: เปลี่ยนสถานะสินค้าเป็น Approved เปิดแสดงในตลาดประมูล -> รีโหลดแดชบอร์ดแอดมิน
        const res = await api(`/api/admin/auctions/${appAucBtn.dataset.approveAuction}/review`, {
          method: "POST", body: JSON.stringify({ approve: true })
        });
        showToast(res.message);
        await loadAdminDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    // ฝั่งแอดมิน: ปฏิเสธสินค้าประมูล (พร้อมระบุเหตุผลให้ผู้ขายแก้ไข)
    const rejAucBtn = target.closest("[data-reject-auction]");
    if (rejAucBtn) {
      showReasonModal({
        title: `ปฏิเสธสินค้า: ${rejAucBtn.dataset.name}`,
        subtitle: "ผู้ขายจะเห็นเหตุผลที่ปฏิเสธในหน้าสินค้าของฉัน",
        label: "เหตุผลการปฏิเสธสินค้า",
        placeholder: "เช่น ภาพถ่ายไม่ชัดเจนหรือรายละเอียดไม่ครบถ้วน",
        submitText: "ยืนยันปฏิเสธสินค้า",
        onSubmit: async reason => {
          // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
          // Endpoint: POST /api/admin/auctions/{itemId}/review -> AdminController.ReviewAuction
          // ข้อมูลที่ส่ง: JSON `{ approve: false, reason }`
          // ผลลัพธ์จากหลังบ้าน: เปลี่ยนสถานะสินค้าเป็น Rejected พร้อมเหตุผล -> รีโหลดแดชบอร์ดแอดมิน
          const res = await api(`/api/admin/auctions/${rejAucBtn.dataset.rejectAuction}/review`, {
            method: "POST", body: JSON.stringify({ approve: false, reason })
          });
          showToast(res.message);
          await loadAdminDashboard();
        }
      });
      return;
    }

    // ฝั่งแอดมิน: ระงับบัญชีผู้ใช้ (พร้อมระบุเหตุผล)
    const suspBtn = target.closest("[data-suspend-user]");
    if (suspBtn) {
      showReasonModal({
        title: `ระงับบัญชี: ${suspBtn.dataset.name}`,
        subtitle: "ผู้ใช้ที่ถูกระงับจะไม่สามารถเข้าสู่ระบบหรือทำรายการได้",
        label: "เหตุผลการระงับบัญชี",
        placeholder: "เช่น ละเมิดกติกาการประมูลซ้ำหลายครั้ง",
        submitText: "ยืนยันระงับบัญชี",
        onSubmit: async reason => {
          // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
          // Endpoint: POST /api/admin/users/{userId}/suspend -> AdminController.SuspendUser
          // ข้อมูลที่ส่ง: JSON `{ suspend: true, reason }`
          // ผลลัพธ์จากหลังบ้าน: ตั้งค่า `IsSuspended = true` และบันทึกเหตุผล -> รีโหลดแดชบอร์ดแอดมิน
          const res = await api(`/api/admin/users/${suspBtn.dataset.suspendUser}/suspend`, {
            method: "POST", body: JSON.stringify({ suspend: true, reason })
          });
          showToast(res.message);
          await loadAdminDashboard();
        }
      });
      return;
    }

    // ฝั่งแอดมิน: ปลดระงับบัญชีผู้ใช้ให้กลับมาใช้งานได้ปกติ
    const unsuspBtn = target.closest("[data-unsuspend-user]");
    if (unsuspBtn) {
      try {
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
        // Endpoint: POST /api/admin/users/{userId}/suspend -> AdminController.SuspendUser
        // ข้อมูลที่ส่ง: JSON `{ suspend: false }`
        // ผลลัพธ์จากหลังบ้าน: ตั้งค่า `IsSuspended = false` -> รีโหลดแดชบอร์ดแอดมิน
        const res = await api(`/api/admin/users/${unsuspBtn.dataset.unsuspendUser}/suspend`, {
          method: "POST", body: JSON.stringify({ suspend: false })
        });
        showToast(res.message);
        await loadAdminDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    // ฝั่งแอดมิน: เติมข้อมูลหมวดหมู่เดิมลงใน `#adminCategoryForm` เพื่อเข้าสู่โหมดแก้ไข
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

    // ฝั่งแอดมิน: ลบหรือปิดใช้งานหมวดหมู่สินค้า (ถ้ามีสินค้าผูกอยู่ หลังบ้านจะ Soft-disable ให้แทนการลบจริง)
    const delCatBtn = target.closest("[data-delete-cat]");
    if (delCatBtn) {
      try {
        // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
        // Endpoint: DELETE /api/admin/categories/{id} -> AdminController.DeleteCategory
        // ข้อมูลที่ส่ง: Route Parameter `id` (+ Auth Cookie สิทธิ์ Admin)
        // ผลลัพธ์จากหลังบ้าน: ลบหรือปิดการใช้งานหมวดหมู่ คืน JSON `{ message }` -> รีโหลดแดชบอร์ดแอดมิน
        const res = await api(`/api/admin/categories/${delCatBtn.dataset.deleteCat}`, { method: "DELETE" });
        showToast(res.message);
        await loadAdminDashboard();
      } catch (err) { showToast(err.message, true); }
      return;
    }

    // ฝั่งแอดมิน: เปิด Modal ตรวจสอบแชทและจัดการเคสข้อพิพาท
    const inspDispBtn = target.closest("[data-inspect-dispute]");
    if (inspDispBtn) {
      await openDisputeInspector(Number(inspDispBtn.dataset.inspectDispute));
      return;
    }

    // ฝั่งฝ่ายการเงิน: ปล่อยเงินโอนสุทธิให้ผู้ขาย (หลังยืนยันส่งมอบ/รับสินค้าและไม่มีข้อพิพาทค้าง)
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
          // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
          // Endpoint: POST /api/finance/orders/{orderId}/release -> FinanceController.ReleasePayout
          // ข้อมูลที่ส่ง: JSON `{ note }` + Route Parameter `orderId`
          // ผลลัพธ์จากหลังบ้าน: เปลี่ยนสถานะ `PayoutStatus = Released` + บันทึก Audit Log คืน JSON `{ message }` -> รีโหลดแดชบอร์ดการเงิน
          const res = await api(`/api/finance/orders/${relBtn.dataset.financeRelease}/release`, {
            method: "POST", body: JSON.stringify({ note })
          });
          showToast(res.message);
          await loadFinanceDashboard();
        }
      });
      return;
    }

    // ฝั่งฝ่ายการเงิน: คืนเงินให้ผู้ซื้อ (กรณีข้อพิพาทหรือยกเลิกออเดอร์)
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
          // 🔗 [จุดเชื่อมต่อ หน้าบ้าน -> หลังบ้าน]
          // Endpoint: POST /api/finance/orders/{orderId}/refund -> FinanceController.RefundOrder
          // ข้อมูลที่ส่ง: JSON `{ note }` + Route Parameter `orderId`
          // ผลลัพธ์จากหลังบ้าน: เปลี่ยนสถานะการชำระเงินและปล่อยเงินเป็น Refunded + Audit Log คืน JSON `{ message }` -> รีโหลดแดชบอร์ดการเงิน
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

  // ==================== 17. ตัวนับเวลาถอยหลังแบบเรียลไทม์ (LIVE COUNTDOWN TICKER) ====================
  // ทำงานทุก 1 วินาที เพื่ออัปเดตข้อความเวลาคงเหลือบนการ์ดสินค้าและใน Modal ตามเวลาจริง `endAt` (UTC ISO)
  // และเมื่อมีสินค้าใดเพิ่งหมดเวลา จะล็อกฟอร์มเสนอราคาทันทีแล้วหน่วง 600ms เพื่อซิงก์สถานะปิดประมูลจากหลังบ้าน
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

    // ล็อกช่องกรอกและปุ่มเสนอราคาภายใน Modal สินค้าที่เปิดค้างอยู่ทันทีเมื่อหมดเวลา
    const openBidForm = document.getElementById("bidForm");
    if (openBidForm && openBidForm.dataset.endAt && parseUtcDate(openBidForm.dataset.endAt).getTime() <= Date.now()) {
      openBidForm.querySelectorAll("input, button").forEach(ctrl => { ctrl.disabled = true; });
      const btn = openBidForm.querySelector("button[type='submit']");
      if (btn) btn.textContent = "ปิดประมูลแล้ว";
      anyExpiredJustNow = true;
    }

    // ป้องกันการยิงรีเฟรชซ้ำซ้อนด้วยแฟล็ก `expirySyncPending` แล้วค่อยดึงข้อมูลปิดประมูลล่าสุดจากหลังบ้าน
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

  // ==================== 18. เริ่มต้นการทำงานของแอปพลิเคชัน (APP BOOT / INITIALIZATION) ====================
  // 1) แสดงวันที่ปัจจุบันภาษาไทยบนแถบส่วนหัวของหน้าแรก
  document.getElementById("todayDate").textContent = new Intl.DateTimeFormat("th-TH", {
    weekday: "long", day: "numeric", month: "long", year: "numeric"
  }).format(new Date());
  // 2) ตั้งค่าวันเวลาเริ่มต้น/สิ้นสุดประมูลตั้งต้นในฟอร์มลงขายสินค้า
  initListingDates();
  // 3) ตรวจสอบ Session ผู้ใช้จากหลังบ้าน (`GET /api/auth/me`) และโหลดหน้าแรกของบทบาทที่เหมาะสม
  refreshSession(true).catch(err => showToast(err.message, true));
})();
