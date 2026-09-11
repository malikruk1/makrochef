// Mock data for local rendering of all 6 screens x 3 states (TASKS.md 8.2 gate).
// Numbers are illustrative only - never shown in the real app without a traceable MCP source.
const MOCKS = {
  profile: {
    protein: { consumed: 62, target: 105, gapLabel: "бракує 43 г" },
    sugar: { consumed: 58, target: 25, gapLabel: "на 33 г більше норми" },
    coveragePercent: 63,
    normSource: "розраховано з калорійності чеків",
    sourceBar: [
      { label: "офлайн чеки", percent: 55, color: "var(--accent)" },
      { label: "онлайн замовлення", percent: 25, color: "var(--accent-soft)" },
      { label: "Open Food Facts", percent: 12, color: "var(--text-compare)" },
      { label: "немає даних", percent: 8, color: "var(--border-card)" },
    ],
  },
  cart: {
    usualTotal: 842,
    optimizedTotal: 811,
    deltaPercent: -3.7,
    swaps: [
      { old: "Йогурт Активіа 2.9%", new: "Йогурт Danone Protein", gain: "+9 г білка, −11 г цукру, −3 ₴", good: true },
      { old: "Батон нарізний", new: "Хліб цільнозерновий", gain: "+4 г білка, −2 г цукру", good: true },
      { old: "Сир твердий 45%", new: "Сир твердий 30%", gain: "−6 г жиру, +2 ₴", good: false },
    ],
    priceBreakdown: [
      { label: "Товари", amount: 850, isDiscount: false },
      { label: "Акції", amount: -39, isDiscount: true },
      { label: "Доставка", amount: 0, isDiscount: false },
    ],
    total: 811,
  },
  reoptimization: {
    droppedProduct: "Йогурт Danone Protein",
    reason: "немає в наявності",
    newBasketDiffCount: 3,
  },
  checkout: {
    total: 811,
    webLink: "https://silpo.ua/checkout/demo",
  },
  weekOverWeek: {
    isRetrospective: true,
    lastWeekGap: 61,
    thisWeekGap: 43,
  },
};
