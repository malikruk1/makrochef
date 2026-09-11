const SCREENS = ["1", "2", "3", "4", "5", "6"];
const STATES = ["data", "empty", "loading", "error"];
let currentScreen = "1";
let currentState = "data";

function renderNav() {
  const nav = document.getElementById("devNav");
  nav.innerHTML = "";
  for (const id of SCREENS) {
    const el = document.getElementById(`screen-${id}`);
    const btn = document.createElement("button");
    btn.textContent = el.dataset.title;
    btn.className = id === currentScreen ? "active" : "";
    btn.onclick = () => { currentScreen = id; render(); };
    nav.appendChild(btn);
  }

  const stateSwitch = document.getElementById("devStateSwitch");
  stateSwitch.innerHTML = "";
  for (const state of STATES) {
    const btn = document.createElement("button");
    btn.textContent = state;
    btn.className = state === currentState ? "active" : "";
    btn.onclick = () => { currentState = state; render(); };
    stateSwitch.appendChild(btn);
  }
}

function render() {
  renderNav();
  for (const id of SCREENS) {
    const el = document.getElementById(`screen-${id}`);
    el.hidden = id !== currentScreen;
  }

  const el = document.getElementById(`screen-${currentScreen}`);
  if (currentState === "empty") {
    el.innerHTML = renderEmpty(currentScreen);
  } else if (currentState === "loading") {
    el.innerHTML = renderLoading();
  } else if (currentState === "error") {
    el.innerHTML = renderError();
  } else {
    el.innerHTML = renderScreens[currentScreen]();
  }
}

function renderLoading() {
  return `<div class="skeleton"></div><div class="skeleton"></div><div class="skeleton" style="width:70%"></div>`;
}

function renderError() {
  return `<div class="state-error">
    Не вдалося отримати дані з MCP «Сільпо».<br/>Перевірте підключення.
    <div><button class="retry-btn" onclick="render()">Повторити</button></div>
  </div>`;
}

function renderEmpty(screenId) {
  const messages = {
    1: "Ще не підключено жодного чека.",
    2: "Чеків ще немає — профіль зʼявиться після першого прогону.",
    3: "Кошик порожній.",
    4: "Переоптимізація не потрібна.",
    5: "Немає кошика для оформлення.",
    6: "Замало даних для порівняння тижнів.",
  };
  return `<div class="state-empty">${messages[screenId] ?? "Даних немає."}</div>`;
}

const renderScreens = {
  "1": () => `
    <h1 class="app-title">MakroChef</h1>
    <p style="font-size:14px;line-height:1.5;color:var(--text-2)">
      Читаємо ваші чеки з Сільпо й показуємо, чого реально бракує в раціоні —
      без ручного вводу їжі.
    </p>
    <div style="margin-top:24px">
      <button class="btn btn-primary" style="width:100%">Підключити Сільпо</button>
    </div>
    <p style="font-size:11px;color:var(--text-muted);margin-top:12px">Читаємо офлайн- і онлайн-чеки за останні 3 місяці.</p>
  `,

  "2": () => {
    const p = MOCKS.profile;
    const barSegments = p.sourceBar.map(s => `<span style="width:${s.percent}%;background:${s.color}"></span>`).join("");
    const legend = p.sourceBar.map(s => `<span><span class="dot" style="background:${s.color}"></span>${s.label}</span>`).join("");
    return `
      <h1 class="app-title">Профіль БЖВ</h1>
      <div class="section-title">Покриття чека</div>
      <div class="source-bar">${barSegments}</div>
      <div class="source-legend">${legend}</div>

      <div class="section-title">Метрики</div>
      <div class="metric-card">
        <div class="label">Білок</div>
        <div class="value">${p.protein.consumed} <span class="of">/ ${p.protein.target} г</span></div>
        <div class="gap-line">${p.protein.gapLabel}</div>
      </div>
      <div class="metric-card">
        <div class="label">Вільні цукри</div>
        <div class="value">${p.sugar.consumed} <span class="of">/ ${p.sugar.target} г</span></div>
        <div class="gap-line">${p.sugar.gapLabel}</div>
      </div>
      <p style="font-size:11px;color:var(--text-muted)">Джерело норми: ${p.normSource}. Покриття даних: ${p.coveragePercent}%.</p>
    `;
  },

  "3": () => {
    const c = MOCKS.cart;
    const swaps = c.swaps.map(s => `
      <div class="swap-row">
        <div class="swap-old">${s.old}</div>
        <div class="swap-new">→ ${s.new}</div>
        <div class="swap-gain ${s.good ? "" : "compromise"}">${s.gain}</div>
      </div>`).join("");
    const priceLines = c.priceBreakdown.map(l => `
      <div class="price-line ${l.isDiscount ? "discount" : ""}">
        <span>${l.label}</span><span>${l.amount > 0 ? "" : l.isDiscount ? "−" : ""}${Math.abs(l.amount)} ₴</span>
      </div>`).join("");

    return `
      <h1 class="app-title">Ваш кошик</h1>
      <div class="compare-card">
        <div class="compare-row">
          <div><div class="compare-number">${c.usualTotal} ₴</div><div class="delta">звичний</div></div>
          <div class="arrow">→</div>
          <div><div class="compare-number" style="color:var(--accent-text)">${c.optimizedTotal} ₴</div><div class="delta">оптимізований</div></div>
        </div>
        <div class="compare-delta delta good">було → стало (${c.deltaPercent}%)</div>
      </div>

      <div class="section-title">Свопи</div>
      <div class="compare-card">${swaps}</div>

      <div class="section-title">Склад ціни</div>
      <div class="price-breakdown">
        ${priceLines}
        <div class="price-line total"><span>До сплати</span><span>${c.total} ₴</span></div>
      </div>

      <div class="bottom-bar">
        <button class="btn btn-secondary">Свопи</button>
        <button class="btn btn-primary">До оформлення</button>
      </div>
    `;
  },

  "4": () => {
    const r = MOCKS.reoptimization;
    return `
      <h1 class="app-title">Переоптимізація</h1>
      <div class="banner"><b>${r.droppedProduct}</b> — ${r.reason}. Перерахували кошик цілком.</div>
      <p style="font-size:13px;color:var(--text-2)">Кошик відрізняється від попереднього на ${r.newBasketDiffCount} позиції — бюджет утримано, дефіцит не зріс.</p>
      <div class="bottom-bar">
        <button class="btn btn-primary" style="width:100%">Переглянути новий кошик</button>
      </div>
    `;
  },

  "5": () => {
    const co = MOCKS.checkout;
    return `
      <h1 class="app-title">Checkout</h1>
      <div class="price-breakdown">
        <div class="price-line total"><span>До сплати</span><span>${co.total} ₴</span></div>
      </div>
      <p style="font-size:12px;color:var(--text-muted);margin-top:12px">Наступного тижня перевіримо, чи скоротився дефіцит.</p>
      <div class="bottom-bar">
        <button class="btn btn-primary" style="width:100%">Оформити замовлення</button>
      </div>
    `;
  },

  "6": () => {
    const w = MOCKS.weekOverWeek;
    return `
      <h1 class="app-title">Тиждень до тижня</h1>
      ${w.isRetrospective ? '<p style="font-size:11px;color:var(--text-muted)">Ретроспектива на історичних даних.</p>' : ""}
      <div class="compare-card">
        <div class="compare-row">
          <div><div class="compare-number">${w.lastWeekGap} г</div><div class="delta">було</div></div>
          <div class="arrow">→</div>
          <div><div class="compare-number" style="color:var(--success)">${w.thisWeekGap} г</div><div class="delta">стало</div></div>
        </div>
        <div class="compare-delta delta good">дефіцит білка скоротився</div>
      </div>
    `;
  },
};

render();
