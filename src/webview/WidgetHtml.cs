// ============================================================================
// WidgetHtml.cs —— 任务栏挂件的界面（内嵌 HTML）
//
// 设计来源：汽水音乐 3.5.1 任务栏迷你播放条（taskbarWidget）的原始 CSS/DOM
//   · 尺寸/圆角/内边距/字号/字重/颜色/过渡时长与缓动，全部沿用原版数值
//   · 结构沿用原版：拖拽热区(22px) → 封面位(28×28) → 信息区(标题行+副行)
//     → 右侧按钮位(32×32) → 右上关闭位
// 内容替换（音乐 → 流量）：
//   · 封面位       → 实时下载速率曲线（canvas）
//   · 标题行(10px/500) → ↓ 下载速率
//   · 副行(11px/600)   → ↑ 上传速率（沿用原版歌词"卡拉OK"双层叠加机制做进度点亮）
//   · 悬停替换区   → 总下载 / 总上传 / 峰值（沿用原版 variant1 hover 时信息区淡出、
//                     操作区淡入的机制）
//   · 右侧按钮位   → 环形速率表（速率占满速基准的比例 + 中心紧凑数值）
//   · 右上关闭位   → 退出（沿用原版 4px 内缩、默认 20% 白、hover 纯白）
// ============================================================================

namespace TrafficWidgetWeb;

static class WidgetHtml
{
    public const string Html = """
<!DOCTYPE html>
<html lang="zh-CN" data-theme="dark">
<head>
<meta charset="utf-8">
<title>流量监控</title>
<style>
:root{
  font-family:MiSans,-apple-system,BlinkMacSystemFont,"Segoe UI",Helvetica,"PingFang SC","Microsoft YaHei UI","Microsoft YaHei",Arial,Avenir,Inter,sans-serif;
  font-size:16px;font-weight:400;color:#ffffffd9;
  font-synthesis:none;text-rendering:optimizeLegibility;
  -webkit-font-smoothing:antialiased;-moz-osx-font-smoothing:grayscale;
  -webkit-text-size-adjust:100%;-webkit-user-select:none;user-select:none;
  color-scheme:dark;
}
*{box-sizing:border-box;margin:0;padding:0}
html,body{width:100%;height:100%;overflow:hidden;background:transparent}
.wrapper{width:100%;height:100%;display:flex}

/* ---- 容器：原版 padding-inline 22px 12px、圆角 8px、0.5px 透明描边、0.1s ease-out ---- */
.container{
  position:relative;width:100%;height:100%;
  padding-inline:22px 12px;display:flex;border-radius:8px;
  border:.5px solid transparent;
  transition-property:border-color,background-color;
  transition-duration:.1s;transition-timing-function:ease-out;
}
/* 原版 data-hover 状态：贴靠任务栏时近乎全透明，仅 hover 浮现极淡底 + 描边 */
.container:hover{border-color:rgba(255,255,255,.04);background-color:rgba(255,255,255,.04)}
html[data-theme="light"] .container:hover{border-color:rgba(0,0,0,.08);background-color:rgba(255,255,255,.30)}

/* ---- 左侧 22px 拖拽热区（原版 -webkit-app-region:drag，WebView2 不支持，改为手动上报位移） ---- */
.drag-handler-wrapper{
  position:absolute;top:0;bottom:0;left:0;width:22px;
  display:flex;align-items:center;justify-content:center;
  opacity:0;transition:opacity .1s ease-out;cursor:grab;
}
.container:hover .drag-handler-wrapper{opacity:1}
.drag-handler-wrapper.dragging{cursor:grabbing}
.drag-handler{
  width:2px;height:16px;border-radius:9999px;
  background:rgba(255,255,255,.12);transition:background .1s ease-out;
}
.drag-handler-wrapper:hover .drag-handler,
.drag-handler-wrapper.dragging .drag-handler{background:rgba(255,255,255,.5)}
html[data-theme="light"] .drag-handler{background:rgba(0,0,0,.12)}

/* ---- 封面位 28×28 / 圆角 4px（原版专辑封面 → 实时速率曲线） ---- */
.cover-wrapper{display:flex;align-items:center;justify-content:center}
.cover{position:relative;width:28px;height:28px;border-radius:4px;overflow:hidden;background:rgba(255,255,255,.10)}
html[data-theme="light"] .cover{background:rgba(0,0,0,.08)}
.cover canvas{position:absolute;inset:0;width:100%;height:100%;display:block}
.cover .glare{position:absolute;inset:0;background:rgba(255,255,255,.12);opacity:0;transition:opacity .12s ease-out}
.cover:hover .glare{opacity:1}

/* ---- 信息区：标题行 10px/500、副行 11px/600 ---- */
/* 信息层与 hover 操作层用 grid 同格叠放：格子宽 = 两层内容较宽者，
   右侧环形表被自然推远，消除操作文字压到环形表/关闭键的重叠 */
.main-wrapper{position:relative;display:grid;align-items:center;margin-left:10px}
.info-wrapper{
  grid-area:1/1;
  display:flex;flex-direction:column;justify-content:space-between;
  max-height:32px;
  transition:opacity .1s ease-out .08s, visibility 0s linear .08s;
}
/* 原版 variant1：hover 时信息区淡出让位给操作区；
   visibility 接力（先淡出→再淡入），杜绝两层半透明文字叠在一起 */
.container:hover .info-wrapper{opacity:0;visibility:hidden;transition:opacity .1s ease-out, visibility 0s linear .1s}
.title-wrapper{display:flex;align-items:center;gap:4px}
.title{
  min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;
  font-size:10px;font-weight:500;line-height:14px;color:rgba(255,255,255,.5);
}
html[data-theme="light"] .title{color:rgba(0,0,0,.5)}
.title .arw{color:#5AD1FF}
.title .num{color:rgba(255,255,255,.92)}
html[data-theme="light"] .title .num{color:rgba(0,0,0,.85)}
.title .unit{color:rgba(255,255,255,.45)}
html[data-theme="light"] .title .unit{color:rgba(0,0,0,.45)}

/* 副行：沿用原版歌词双层叠加（base 底色 + played 高亮层按宽度裁切） */
.lyric-wrapper{overflow:hidden;font-size:11px;font-weight:600;line-height:15px;height:1lh}
.lyric-viewport{width:100%;overflow:hidden}
.lyric-track{position:relative;width:fit-content;min-width:100%}
.lyric{white-space:nowrap}
.lyric--base{color:rgba(255,255,255,.5)}
html[data-theme="light"] .lyric--base{color:rgba(0,0,0,.5)}
.lyric--played{position:absolute;inset:0 auto 0 0;overflow:hidden;color:#8CF0BE;pointer-events:none}

/* ---- 操作区（与信息区 grid 同格叠放，hover 才淡入） ---- */
.action-wrapper{
  grid-area:1/1;display:flex;flex-direction:column;justify-content:space-between;
  max-height:32px;pointer-events:none;opacity:0;visibility:hidden;
  transition:opacity .1s ease-out, visibility 0s linear .1s;
}
.container:hover .action-wrapper{
  opacity:1;visibility:visible;pointer-events:auto;
  transition:opacity .1s ease-out .08s, visibility 0s linear .08s;
}
.act-row1{
  display:flex;align-items:center;gap:10px;white-space:nowrap;
  font-size:10px;font-weight:500;line-height:14px;color:rgba(255,255,255,.5);
}
html[data-theme="light"] .act-row1{color:rgba(0,0,0,.5)}
.act-row1 b{font-weight:500}
.act-row1 .vd{color:#7FD8FF}
.act-row1 .vu{color:#8CF0BE}
.act-row2{font-size:11px;font-weight:600;line-height:15px;color:rgba(255,255,255,.85);white-space:nowrap}
html[data-theme="light"] .act-row2{color:rgba(0,0,0,.8)}

/* ---- 右侧按钮位 32×32（原版收藏按钮 → 环形速率表） ---- */
.side-wrapper{display:flex;align-items:center;justify-content:center;padding-left:12px}
.ring{width:32px;height:32px;display:block}
/* 环形表底色/中心数值随主题适配（CSS 色覆盖 SVG 呈现属性，两模式统一生效） */
html[data-theme="light"] .ring-track{stroke:rgba(0,0,0,.14)}
html[data-theme="light"] #ringTxt{fill:rgba(0,0,0,.78)}

/* ---- 右上关闭位（原版 6×6 图标、内缩 4px、默认 20% 白、hover 纯白） ---- */
.close-wrapper{
  position:absolute;top:1px;right:1px;padding:3px;display:flex;
  color:rgba(255,255,255,.2);opacity:0;transition:opacity .1s ease-out;cursor:pointer;
}
html[data-theme="light"] .close-wrapper{color:rgba(0,0,0,.2)}
.container:hover .close-wrapper{opacity:1}
.close-wrapper:hover{color:rgb(255,255,255)}
html[data-theme="light"] .close-wrapper:hover{color:rgb(0,0,0)}

/* ================= Island（灵动岛）模式：贴屏幕顶中央的半透明胶囊 =================
   外观：半透明底色直接透出桌面（窗口本身透明合成，backdrop-filter 看不到桌面、
   且属 CPU 密集效果，均不用）；底色/描边/文字阴影做成 CSS 变量，随 data-theme
   深浅色自动切换 —— 文字颜色由既有主题规则联动，实现"随背景自动适配"。
   展开：hover 时宿主把窗口高度 40→88 DIP 弹性动画；详情区（总/峰值）常驻第二行，
   信息行固定在上 40px 内 —— 收起/展开全程无跳动。 */
html[data-mode="island"]{
  --isl-bg-now:rgba(10,10,14,.66);
  --isl-border:transparent;--isl-shadow:0 1px 3px rgba(0,0,0,.5);
}
html[data-mode="island"][data-theme="light"]{
  --isl-bg-now:rgba(246,246,248,.72);
  --isl-border:transparent;--isl-shadow:none;
}
html[data-mode="island"] .container:hover{--isl-bg-now:rgba(10,10,14,.80)}
html[data-mode="island"][data-theme="light"] .container:hover{--isl-bg-now:rgba(246,246,248,.84)}
/* 屏幕顶边与岛体交界的凹弧翼半径（DIP）；窗口两侧各留同宽透明区（.wrapper padding），
   翼片画在岛体外、与顶边/侧边双相切，形成 iPhone 灵动岛式外扩过渡。
   注意：岛体必须 border-width:0——0.5px 边框会把背景和翼片整体下移，顶边露出透缝 */
html[data-mode="island"] .wrapper{padding-inline:18px}
html[data-mode="island"] .container{
  background:var(--isl-bg-now);border-radius:0 0 18px 18px;
  border-width:0;text-shadow:var(--isl-shadow);
  flex-direction:column;align-items:stretch;padding-bottom:6px;
}
/* 凹弧翼：方形区域减去左下角圆 = 与屏幕顶边、岛体侧边双相切的凹面；
   颜色用 --isl-bg-now 与岛体同步（含 hover 加深），radial-gradient 软边抗锯齿 */
html[data-mode="island"] .container::before,
html[data-mode="island"] .container::after{
  content:'';position:absolute;top:0;width:18px;height:18px;pointer-events:none;
}
html[data-mode="island"] .container::before{
  left:-18px;
  background:radial-gradient(circle at 0 100%, transparent 17.5px, var(--isl-bg-now) 18.5px);
}
html[data-mode="island"] .container::after{
  right:-18px;
  background:radial-gradient(circle at 100% 100%, transparent 17.5px, var(--isl-bg-now) 18.5px);
}
/* 顶行包裹层：任务栏模式 display:contents 完全透明、零影响；Island 模式变横排行 */
.island-row{display:contents}
html[data-mode="island"] .island-row{display:flex;align-items:center;justify-content:center;flex:0 0 auto;height:32px}
/* 详情区改为「第二行」：绝对定位到顶行下方（相对 container），
   收起态（窗口高 40）时被窗口裁掉不可见，展开动画时随窗口增高自然露出——灵动岛效果 */
html[data-mode="island"] .main-wrapper{position:static}
html[data-mode="island"] .action-wrapper{
  position:absolute;inset:36px 12px auto 22px;opacity:1;visibility:visible;pointer-events:auto;
  max-height:none;justify-content:flex-start;align-items:center;gap:2px;transition:none;
}
html[data-mode="island"] .container:hover .info-wrapper{opacity:1;visibility:visible}
/* 宽度防环：Island 下行包裹层被容器拉伸（align-items:stretch），歌词链的百分比
   min-width:100% 会解析成容器宽 → 测量值反馈把窗口越拉越宽。改 auto 让宽度由文本决定 */
html[data-mode="island"] .lyric-viewport{width:auto}
html[data-mode="island"] .lyric-track{min-width:0;width:max-content}
/* 同理：详情行在 column flex 中会被 stretch 成容器宽，scrollWidth 测出的是容器宽
   而非内容宽 → measureWidth 正反馈把窗口顶到上限。width:max-content 锁定为文本宽 */
html[data-mode="island"] .act-row1,
html[data-mode="island"] .act-row2{width:max-content;max-width:100%}
</style>
</head>
<body>
<div class="wrapper">
  <div class="container">

    <div class="drag-handler-wrapper" id="dragZone"><div class="drag-handler"></div></div>

    <div class="island-row">
    <div class="cover-wrapper">
      <div class="cover"><canvas id="spark"></canvas><div class="glare"></div></div>
    </div>

    <div class="main-wrapper">
      <div class="info-wrapper">
        <div class="title-wrapper">
          <div class="title"><span class="arw">&#8595;</span> <span class="num" id="dlNum">0</span> <span class="unit" id="dlUnit">B/s</span></div>
        </div>
        <div class="lyric-wrapper">
          <div class="lyric-viewport">
            <div class="lyric-track">
              <div class="lyric lyric--base" id="ulBase">&#8593; 0 B/s</div>
              <div class="lyric lyric--played" id="ulPlayed">&#8593; 0 B/s</div>
            </div>
          </div>
        </div>
      </div>
      <div class="action-wrapper">
        <div class="act-row1">
          <span>&#8595;总 <b class="vd" id="totDl">0B</b></span>
          <span>&#8593;总 <b class="vu" id="totUl">0B</b></span>
        </div>
        <div class="act-row2">峰值 <span id="pk">0 B/s</span></div>
      </div>
    </div>

    <div class="side-wrapper">
      <svg class="ring" viewBox="0 0 32 32">
        <defs>
          <linearGradient id="rg" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stop-color="#5AD1FF"/><stop offset="1" stop-color="#A98BFF"/>
          </linearGradient>
        </defs>
        <circle class="ring-track" cx="16" cy="16" r="12.5" fill="none" stroke="rgba(255,255,255,.12)" stroke-width="2.5"/>
        <circle id="ringArc" cx="16" cy="16" r="12.5" fill="none" stroke="url(#rg)" stroke-width="2.5"
                stroke-linecap="round" stroke-dasharray="0 78.54" transform="rotate(-90 16 16)"/>
        <text id="ringTxt" x="16" y="17" text-anchor="middle" font-size="8.5" font-weight="700"
              fill="rgba(255,255,255,.78)">0</text>
      </svg>
    </div>
    </div><!-- /island-row -->

    <div class="close-wrapper" id="closeBtn">
      <svg xmlns="http://www.w3.org/2000/svg" width="6" height="6" fill="none" viewBox="0 0 6 6"><path fill="currentColor" d="M4.654.904a.312.312 0 1 1 .441.442L3.441 3l1.654 1.654a.312.312 0 1 1-.441.442L3 3.44 1.345 5.096a.312.312 0 1 1-.441-.442L2.558 3 .904 1.346a.312.312 0 1 1 .441-.442L3 2.56z"/></svg>
    </div>

  </div>
</div>
<script>
(function(){
  "use strict";

  // 满速基准：30 MB/s（即环形表 100% 对应的速率）
  var MAX = 30 * 1024 * 1024;
  var HIST = 100;                    // 曲线保留的采样点数（500ms 一点 → 约 50 秒窗口）
  var hist = new Array(HIST).fill(0);

  var cv = document.getElementById('spark');
  var ctx = cv.getContext('2d');
  var RING_C = 2 * Math.PI * 12.5;    // 环形表周长

  function fmtSpeed(b){
    b = b > 0 ? b : 0;
    if (b < 1024)        return [Math.round(b).toString(), 'B/s'];
    if (b < 1048576)     return [(b / 1024).toFixed(1), 'KB/s'];
    if (b < 1073741824)  return [(b / 1048576).toFixed(1), 'MB/s'];
    return [(b / 1073741824).toFixed(2), 'GB/s'];
  }
  function fmtCompact(b){
    b = b > 0 ? b : 0;
    if (b < 1024)    return Math.round(b).toString();
    if (b < 1048576) return Math.round(b / 1024) + 'K';
    if (b < 1073741824){
      var m = b / 1048576;
      return (m < 10 ? m.toFixed(1) : Math.round(m).toString()) + 'M';
    }
    return (b / 1073741824).toFixed(1) + 'G';
  }
  function fmtTotal(b){
    b = b > 0 ? b : 0;
    if (b < 1024)        return Math.round(b) + 'B';
    if (b < 1048576)     return Math.round(b / 1024) + 'KB';
    if (b < 1073741824)  return (b / 1048576).toFixed(1) + 'MB';
    return (b / 1073741824).toFixed(2) + 'GB';
  }
  function ratio(v, max){
    var r = v / max;
    return r > 1 ? 1 : (r < 0 ? 0 : r);
  }

  // ---- 速率曲线 ----
  function sizeCanvas(){
    var r = window.devicePixelRatio || 1;
    cv.width  = Math.round(28 * r);
    cv.height = Math.round(28 * r);
  }
  function drawChart(){
    var w = cv.width, h = cv.height, r = window.devicePixelRatio || 1;
    ctx.clearRect(0, 0, w, h);

    var mx = 0;
    for (var i = 0; i < HIST; i++) if (hist[i] > mx) mx = hist[i];
    if (mx < 65536) mx = 65536;                 // 下限 64KB/s，避免低速时曲线被噪声拉满

    var step = w / (HIST - 1), top = 2 * r, bot = h - 2 * r, y;

    ctx.beginPath();
    ctx.moveTo(0, h);
    for (var i = 0; i < HIST; i++){
      y = bot - (bot - top) * Math.pow(ratio(hist[i], mx), 0.65);
      ctx.lineTo(i * step, y);
    }
    ctx.lineTo(w, h);
    ctx.closePath();
    var g = ctx.createLinearGradient(0, 0, 0, h);
    g.addColorStop(0, 'rgba(90,209,255,.55)');
    g.addColorStop(1, 'rgba(90,209,255,0)');
    ctx.fillStyle = g;
    ctx.fill();

    ctx.beginPath();
    for (var j = 0; j < HIST; j++){
      y = bot - (bot - top) * Math.pow(ratio(hist[j], mx), 0.65);
      if (j === 0) ctx.moveTo(0, y); else ctx.lineTo(j * step, y);
    }
    ctx.strokeStyle = 'rgba(150,225,255,.95)';
    ctx.lineWidth = 1.2 * r;
    ctx.lineJoin = 'round';
    ctx.stroke();
  }

  // ---- 数据更新 ----
  function update(d){
    var s = fmtSpeed(d.dl);
    document.getElementById('dlNum').textContent   = s[0];
    document.getElementById('dlUnit').textContent  = s[1];

    var us = fmtSpeed(d.ul);
    var utxt = '\u2191 ' + us[0] + ' ' + us[1];
    var base = document.getElementById('ulBase');
    base.textContent = utxt;
    document.getElementById('ulPlayed').textContent = utxt;

    document.getElementById('totDl').textContent = fmtTotal(d.dlTot);
    document.getElementById('totUl').textContent = fmtTotal(d.ulTot);

    var ps = fmtSpeed(d.pk);
    document.getElementById('pk').textContent = ps[0] + ' ' + ps[1];

    // 环形表：弧长 = 当前下载速率占满速基准比例
    document.getElementById('ringTxt').textContent = fmtCompact(d.dl);
    document.getElementById('ringArc').setAttribute(
      'stroke-dasharray', (RING_C * ratio(d.dl, MAX)).toFixed(2) + ' ' + RING_C.toFixed(2));

    // 副行高亮宽度 = 上传速率占满速基准比例 × 文本宽度（沿用原版歌词裁切机制）
    document.getElementById('ulPlayed').style.width =
      (base.scrollWidth * ratio(d.ul, MAX)).toFixed(1) + 'px';

    hist.push(d.dl);
    if (hist.length > HIST) hist.shift();
    drawChart();
    measureWidth();
  }

  // ---- 内容自适应宽度：固定部件之和 + 信息区自然宽度，变化时上报宿主调整窗口 ----
  // 固定部件：左拖拽区22 + 封面28 + 信息区左边距10 + 右侧内边距12 + 环形表32 + 容器右内边距12 = 116
  var lastW = 0;
  function measureWidth(){
    var main = document.querySelector('.main-wrapper');
    var a1 = document.querySelector('.act-row1');
    var a2 = document.querySelector('.act-row2');
    if (!main || !a1 || !a2) return;   // DOM 未就绪保护，避免抛异常中断上报
    var detailW = Math.max(a1.scrollWidth, a2.scrollWidth);
    // 任务栏模式：hover 操作区覆盖信息区，取三者最大值保证不裁字
    // Island 模式：收起态紧贴顶行宽（不留黑区），展开态才加宽容纳详情行
    var mainW = (island && !document.documentElement.classList.contains('island-on'))
      ? main.offsetWidth
      : Math.max(main.offsetWidth, detailW);
    // Island 模式两侧各留 18px 透明区给凹弧翼（翼画在岛体外、贴屏幕顶边外扩）
    var w = 116 + mainW + (island ? 36 : 0);
    if (Math.abs(w - lastW) >= 1){ lastW = w; post({ t: 'resize', w: w }); }
  }

  function post(o){ try { window.chrome.webview.postMessage(o); } catch (e) {} }

  // ---- 与 C# 宿主通信 ----
  window.chrome.webview.addEventListener('message', function(e){
    var d = e.data;
    if (!d) return;
    if (d.t === 'theme'){
      // 深浅色两模式统一跟随系统：Island 半透明底色的文字色由主题规则联动适配
      document.documentElement.setAttribute('data-theme', d.light ? 'light' : 'dark');
      return;
    }
    if (d.t === 'mode'){
      island = !!d.island;
      document.documentElement.setAttribute('data-mode', island ? 'island' : 'taskbar');
      if (!island)
        document.documentElement.classList.remove('island-on'); // 清除展开标记，避免下次进 Island 误判
      measureWidth();
      return;
    }
    if (d.t === 'data') update(d);
  });

  // ---- Island 模式：hover 上报展开/收起（宿主做窗口高度弹性动画） ----
  var island = false;
  document.addEventListener('mouseover', function(e){
    if (island && !document.documentElement.classList.contains('island-on')){
      document.documentElement.classList.add('island-on');
      post({ t: 'expand', on: true });
      measureWidth();   // 展开态：窗口同步加宽容纳详情行
    }
  });
  document.addEventListener('mouseout', function(e){
    if (!island) return;
    if (e.relatedTarget && document.documentElement.contains(e.relatedTarget)) return;
    document.documentElement.classList.remove('island-on');
    post({ t: 'expand', on: false });
    measureWidth();     // 收起态：窗口收回紧贴顶行宽
  });

  document.getElementById('closeBtn').addEventListener('click', function(){ post({ t: 'close' }); });

  // 右键 → 交给宿主弹出原生菜单
  document.addEventListener('contextmenu', function(e){
    e.preventDefault();
    post({ t: 'menu' });
  });

  // ---- 拖拽：原版靠 -webkit-app-region:drag，WebView2 不支持，这里上报屏幕位移给宿主 ----
  var dz = document.getElementById('dragZone');
  dz.addEventListener('pointerdown', function(e){
    if (e.button !== 0) return;
    e.preventDefault();
    dz.classList.add('dragging');
    try { dz.setPointerCapture(e.pointerId); } catch (err) {}
    var lx = e.screenX, ly = e.screenY;
    function mv(ev){
      var dx = ev.screenX - lx, dy = ev.screenY - ly;
      lx = ev.screenX; ly = ev.screenY;
      if (dx || dy) post({ t: 'drag', dx: dx, dy: dy });
    }
    function up(){
      dz.classList.remove('dragging');
      dz.removeEventListener('pointermove', mv);
      dz.removeEventListener('pointerup', up);
      dz.removeEventListener('pointercancel', up);
    }
    dz.addEventListener('pointermove', mv);
    dz.addEventListener('pointerup', up);
    dz.addEventListener('pointercancel', up);
  });

  sizeCanvas();
  drawChart();
  measureWidth();   // 首帧即按内容定宽
  window.addEventListener('resize', function(){ sizeCanvas(); drawChart(); });

  post({ t: 'ready' });
})();
</script>
</body>
</html>
""";
}
