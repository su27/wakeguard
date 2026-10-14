"use strict";
const { invoke } = window.__TAURI__.core;
const { listen } = window.__TAURI__.event;
const settingsView = new URLSearchParams(location.search).get("view") === "settings";
document.body.classList.toggle("settings-view", settingsView);
const words = {
  en: { modeHeading:"Awake mode",inactive:"System default",keepAwake:"Keep awake",displayOn:"Keep screen on",durationHeading:"Duration",unlimited:"Unlimited",minutes30:"30 min",hour1:"1 hour",hours2:"2 hours",hours4:"4 hours",actionsHeading:"Immediate actions",lock:"Lock computer",screensaver:"Start screen saver",settingsTitle:"WakeGuard Settings",settingsSubtitle:"These preferences apply only to the current Windows user",general:"General",startup:"Start WakeGuard when I sign in to Windows",startupDescription:"You can still start it manually from the Start menu",language:"Language",languageDescription:"Changes apply to the tray interface immediately",about:"About",aboutDescription:"Keeps Windows awake after locking or turning off the display using Windows Power Request API.",close:"Close",settings:"Settings",exit:"Exit",version:"Version",disconnected:"Background service unavailable",otherUser:"Another user is keeping the system awake",help:"These actions do not change the awake mode or duration above",remaining:"remaining",ends:"Ends at",busy:"Applying…",exitAnyway:"Exit anyway",exitWarning:"Could not confirm release. The lease will expire within 75 seconds. Exit anyway?",operationFailed:"Could not complete the operation" },
  zh: { modeHeading:"唤醒状态",inactive:"不保持",keepAwake:"保持唤醒",displayOn:"唤醒且常亮",durationHeading:"保持时间",unlimited:"不限",minutes30:"30 分钟",hour1:"1 小时",hours2:"2 小时",hours4:"4 小时",actionsHeading:"立即操作",lock:"锁定电脑",screensaver:"启动屏幕保护程序",settingsTitle:"WakeGuard 设置",settingsSubtitle:"这些偏好设置仅应用于当前 Windows 用户",general:"常规",startup:"登录 Windows 后自动启动 WakeGuard",startupDescription:"关闭后仍可从开始菜单手动启动",language:"语言",languageDescription:"更改会立即应用到托盘界面",about:"关于",aboutDescription:"通过 Windows Power Request API，在锁屏或屏幕关闭后保持系统唤醒。",close:"关闭",settings:"设置",exit:"退出",version:"版本",disconnected:"后台服务未连接",otherUser:"其他用户正在保持唤醒",help:"不会改变上面的唤醒状态和保持时间",remaining:"剩余",ends:"结束",busy:"正在应用…",exitAnyway:"仍要退出",exitWarning:"后台服务未确认释放。租约会在最多 75 秒后自动失效，仍要退出吗？",operationFailed:"未能完成操作" }
};
let state, busy = false, progress = false, progressTimer;
const $ = id => document.getElementById(id);
const text = () => state?.settings.Language === 0 ? words.zh : words.en;
function notice(message, exit = false) {
  $("notice-text").textContent = message;
  $("force-exit").hidden = !exit;
  $("notice").hidden = false;
}
function render() {
  if (!state) return;
  const t = text();
  document.documentElement.lang = state.settings.Language === 0 ? "zh-CN" : "en";
  document.querySelectorAll("[data-text]").forEach(el => el.textContent = t[el.dataset.text]);
  for (const id of ["settings","exit","help"]) {
    const label = id === "exit" ? `${t.exit} WakeGuard` : t[id];
    $(id).title = label; $(id).setAttribute("aria-label", label);
  }
  $("close-settings").setAttribute("aria-label", t.close);
  $("startup").checked = state.settings.StartWithWindows;
  $("language").value = String(state.settings.Language);
  $("version").textContent = `${t.version} ${state.version}`;
  $("mode-icon").src = `icons/${({Inactive:"inactive",KeepAwake:"awake",KeepAwakeAndDisplayOn:"display"})[state.mode]}.png`;
  document.querySelectorAll("[data-mode]").forEach(button => {
    button.setAttribute("aria-pressed", String(button.dataset.mode === state.mode));
    button.disabled = busy;
  });
  document.querySelectorAll("[data-minutes]").forEach(button => {
    const minutes = button.dataset.minutes === "" ? null : Number(button.dataset.minutes);
    button.setAttribute("aria-pressed", String(minutes === state.selectedMinutes));
    button.disabled = busy || state.mode === "Inactive";
  });
  $("startup").disabled = busy; $("language").disabled = busy;
  updateStatus();
}
function updateStatus() {
  if (!state) return;
  const t = text();
  let status = !state.connected ? t.disconnected : progress ? t.busy : ({Inactive:t.inactive,KeepAwake:t.keepAwake,KeepAwakeAndDisplayOn:t.displayOn})[state.mode];
  if (state.connected && state.mode === "Inactive" && state.effectiveMode !== "Inactive") status = t.otherUser;
  if (state.stopAtUtc && state.mode !== "Inactive" && !progress && state.connected) {
    const remaining = Math.max(0, Math.ceil((Date.parse(state.stopAtUtc) - Date.now()) / 60000));
    const end = new Date(state.stopAtUtc).toLocaleTimeString(document.documentElement.lang, {hour:"2-digit",minute:"2-digit"});
    status += state.settings.Language === 0 ? ` · 剩余 ${remaining} 分钟 · ${end} 结束` : ` · ${remaining} min remaining · Ends at ${end}`;
  }
  $("status").textContent = status;
  if (state.preview) $("status").textContent = `[Preview] ${status}`;
  $("status").title = status;
}
async function action(command, args = {}, exiting = false) {
  if (busy) return;
  busy = true; render();
  progressTimer = setTimeout(() => { progress = true; updateStatus(); }, 2000);
  try {
    const result = await invoke(command, args);
    if (result?.settings) state = result;
  } catch (error) {
    notice(`${exiting ? text().exitWarning : text().operationFailed}\n${String(error)}`, exiting);
    try { state = await invoke("get_state"); } catch { /* Keep the last known state. */ }
  } finally {
    clearTimeout(progressTimer); busy = false; progress = false; render();
  }
}
document.querySelectorAll("[data-mode]").forEach(button => button.addEventListener("click", () => action("set_mode", {mode:button.dataset.mode})));
document.querySelectorAll("[data-minutes]").forEach(button => button.addEventListener("click", () => action("set_duration", {minutes:button.dataset.minutes === "" ? null : Number(button.dataset.minutes)})));
$("settings").addEventListener("click", () => action("open_settings"));
$("exit").addEventListener("click", () => action("quit", {force:false}, true));
$("force-exit").addEventListener("click", () => action("quit", {force:true}, true));
$("lock").addEventListener("click", () => action("lock_computer"));
$("screensaver").addEventListener("click", () => action("start_screensaver"));
$("help").addEventListener("click", () => notice(text().help));
$("dismiss-notice").addEventListener("click", () => $("notice").hidden = true);
for (const id of ["done","close-settings"]) $(id).addEventListener("click", () => invoke("hide_window"));
$("startup").addEventListener("change", () => action("save_settings", {settings:{...state.settings,StartWithWindows:$("startup").checked}}));
$("language").addEventListener("change", () => action("save_settings", {settings:{...state.settings,Language:Number($("language").value)}}));
document.addEventListener("keydown", event => { if (event.key === "Escape") invoke("hide_window"); });
document.addEventListener("contextmenu", event => event.preventDefault());
async function initialize() {
  if (!settingsView) {
    const scale = await invoke("display_scale");
    document.documentElement.style.setProperty("--legacy-font-scale", String(scale));
  }
  await listen("wakeguard-state", event => { state = event.payload; render(); });
  state = await invoke("get_state");
  $(settingsView ? "preferences" : "panel").hidden = false;
  render();
  // Hidden WebViews may suspend requestAnimationFrame. Force layout before
  // signaling readiness without waiting for a frame that cannot be displayed.
  document.documentElement.getBoundingClientRect();
  await Promise.race([document.fonts.ready, new Promise(resolve => setTimeout(resolve, 100))]);
  await invoke("ready");
  if (!settingsView) {
    invoke("refresh").then(value => { state = value; render(); }).catch(() => {});
    setInterval(updateStatus, 1000);
  }
}
initialize().catch(error => { notice(String(error)); invoke("ready").catch(() => {}); });
