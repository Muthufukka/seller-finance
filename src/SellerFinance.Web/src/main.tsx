import React, { useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import { BarChart3, Box, CircleAlert, Download, LayoutDashboard, LogIn, Menu, PackageSearch, RefreshCw, Search, Settings, ShoppingBag, Sparkles, TrendingDown, TrendingUp, WalletCards, X } from "lucide-react";
import "./styles.css";
import { AdminConsole } from "./AdminConsole";
import { KaspiConnections } from "./KaspiConnections";
import { I18nProvider, useI18n } from "./i18n";

type Summary = {
  revenue: number;
  orders: number;
  units: number;
  cogs: number | null;
  grossProfit: number;
  marketplaceFees: number;
  delivery: number;
  operatingExpenses: number;
  operatingProfit: number;
  operatingMarginPct: number | null;
  coveragePct: number;
  isPreliminary: boolean;
};
type Product = {
  id: string;
  sku: string;
  name: string;
  units: number;
  revenue: number;
  cogs: number | null;
  profit: number | null;
  margin: number | null;
  cost: number | null;
  coveragePct?: number;
  directExpenses?: number;
  allocatedOrganizationExpenses?: number;
  productStatus?: "Active" | "Archived";
  category?: string;
  status: string;
};
type Point = { date: string; revenue: number; profit: number };
type ProductPoint = { date: string; units: number; revenue: number; cogs: number | null; marketplaceFees: number; delivery: number; otherVariableCosts: number; expenses: number; operatingProfit: number; operatingMarginPct: number | null; coveragePct: number; isPreliminary: boolean };
type DashboardProblems = {
  missingCosts: { id: string; sku: string; name: string; revenue: number; coveragePct: number }[];
  negativeMargins: { id: string; sku: string; name: string; profit: number; margin: number | null }[];
  syncIssues: { id: string; connectionId: string; storeName: string; errorCode?: string; createdAt: string }[];
  missingCostCount?: number;
  negativeMarginCount?: number;
  syncIssueCount?: number;
  totalCount: number;
};
type Page = "dashboard" | "products" | "orders" | "abc" | "integrations" | "expenses" | "fees" | "exports" | "settings" | "admin";
type Session = {
  userId: string;
  email: string;
  displayName: string;
  organizationId: string;
  organizationName: string;
  timeZone: string;
  currency: string;
  allocateOrganizationExpenses: boolean;
  role: string;
  plan: string;
  trialEndsAt?: string;
  isSaasAdmin?: boolean;
};
type OrganizationOption = { id: string; name: string; role: string };
type Order = {
  id: string;
  externalId?: string;
  date: string;
  status: string;
  amount: number;
  items: number;
  profit?: number;
  fees?: number;
  delivery?: number;
  complete: boolean;
  calculationDateFallback?: boolean;
};

const fallback: { summary: Summary; products: Product[]; timeseries: Point[] } = {
  summary: {
    revenue: 173835,
    orders: 6,
    units: 12,
    cogs: null,
    grossProfit: 83835,
    marketplaceFees: 17798,
    delivery: 3850,
    operatingExpenses: 2500,
    operatingProfit: 49687,
    operatingMarginPct: 28.58,
    coveragePct: 92.53,
    isPreliminary: true,
  },
  products: [
    {
      id: "p1",
      sku: "HOME-101",
      name: "Органайзер для кухни",
      units: 5,
      revenue: 62475,
      cogs: 36000,
      profit: 18865,
      margin: 30.2,
      cost: 7200,
      status: "profitable",
    },
    {
      id: "p2",
      sku: "BEAUTY-220",
      name: "Набор косметичек",
      units: 3,
      revenue: 55470,
      cogs: 26700,
      profit: 22173,
      margin: 40,
      cost: 8900,
      status: "profitable",
    },
    {
      id: "p3",
      sku: "TECH-044",
      name: "Настольная LED-лампа",
      units: 3,
      revenue: 42900,
      cogs: 27300,
      profit: 10400,
      margin: 24.2,
      cost: 9100,
      status: "profitable",
    },
    {
      id: "p4",
      sku: "KIDS-018",
      name: "Развивающий набор",
      units: 1,
      revenue: 12990,
      cogs: null,
      profit: null,
      margin: null,
      cost: null,
      status: "missing-cost",
    },
  ],
  timeseries: [
    { date: "2026-08-06", revenue: 24990, profit: 7166 },
    { date: "2026-08-07", revenue: 18490, profit: 7124 },
    { date: "2026-08-08", revenue: 42900, profit: 10400 },
    { date: "2026-08-09", revenue: 12990, profit: 1051 },
    { date: "2026-08-10", revenue: 37485, profit: 10999 },
    { date: "2026-08-11", revenue: 36980, profit: 13850 },
  ],
};

const money = (v: number | null) => (v === null ? "—" : new Intl.NumberFormat("ru-RU", { maximumFractionDigits: 0 }).format(v) + " ₸");
const pct = (v: number | null) => (v === null ? "—" : v.toFixed(1).replace(".", ",") + "%");

function App() {
  const {t,locale,setLocale}=useI18n();
  const [page, setPage] = useState<Page>("dashboard"),
    [menu, setMenu] = useState(false),
    [loading, setLoading] = useState(true);
  const [period, setPeriod] = useState("30"),
    [customFrom, setCustomFrom] = useState(""),
    [customTo, setCustomTo] = useState(""),
    [completeCostsOnly, setCompleteCostsOnly] = useState(false);
  const [session, setSession] = useState<Session | null>(null),
    [authReady, setAuthReady] = useState(false);
  const [organizations,setOrganizations]=useState<OrganizationOption[]>([]);
  const [summary, setSummary] = useState(fallback.summary),
    [products, setProducts] = useState(fallback.products),
    [points, setPoints] = useState(fallback.timeseries);
  const [orders, setOrders] = useState<Order[]>([]);
  const [focusedProductId,setFocusedProductId]=useState<string>();
  const [dashboardProblems, setDashboardProblems] = useState<DashboardProblems>({missingCosts:[],negativeMargins:[],syncIssues:[],totalCount:0});
  useEffect(() => {
    fetch("/api/v1/session")
      .then(async (r) => (r.ok ? setSession(await r.json()) : setSession(null)))
      .finally(() => setAuthReady(true));
  }, []);
  useEffect(()=>{if(!session)return setOrganizations([]);fetch("/api/v1/organizations",{headers:{"X-Organization-Id":session.organizationId}}).then(r=>r.ok?r.json():[]).then(setOrganizations);},[session?.organizationId]);
  useEffect(() => {
    const invitationToken = new URLSearchParams(location.search).get("invitationToken");
    if (!session || !invitationToken) return;
    const currentHeaders = { "X-Organization-Id": session.organizationId, "Content-Type": "application/json" };
    fetch("/api/v1/invitations/accept", { method: "POST", headers: currentHeaders, body: JSON.stringify({ token: invitationToken }) })
      .then(async (response) => {
        if (!response.ok) throw new Error("invitation");
        const accepted = await response.json();
        return fetch("/api/v1/session", { headers: { "X-Organization-Id": accepted.organizationId } });
      })
      .then(async (response) => {
        if (!response.ok) throw new Error("session");
        setSession(await response.json());
        history.replaceState({}, "", location.pathname);
        setPage("settings");
      })
      .catch(() => {});
  }, [session?.userId]);
  useEffect(() => {
    if (!session) return;
    const headers = { "X-Organization-Id": session.organizationId };
    const range = periodRange(period, customFrom, customTo);
    if (!range) return;
    const query = `?dateFrom=${range.from}&dateTo=${range.to}&completeCostsOnly=${completeCostsOnly}`;
    setLoading(true);
    Promise.all([fetch("/api/v1/analytics/summary" + query, { headers }).then((r) => (r.ok ? r.json() : Promise.reject())), fetch("/api/v1/analytics/products" + query, { headers }).then((r) => (r.ok ? r.json() : Promise.reject())), fetch("/api/v1/analytics/timeseries" + query, { headers }).then((r) => (r.ok ? r.json() : Promise.reject())), fetch("/api/v1/orders" + query, { headers }).then((r) => (r.ok ? r.json() : Promise.reject())),fetch("/api/v1/analytics/problems"+query,{headers}).then(r=>r.ok?r.json():Promise.reject())])
      .then(([s, p, t, o, problems]) => {
        setSummary(s);
        setProducts(p);
        setPoints(t);
        setOrders(o.items ?? o);
        setDashboardProblems(problems);
      })
      .catch(() => {})
      .finally(() => setLoading(false));
  }, [session, period, customFrom, customTo, completeCostsOnly]);
  if (!authReady) return <div className="auth-loading">Seller Finance</div>;
  if (!session) return <AuthScreen onAuthenticated={setSession} />;
  const activeRange = periodRange(period, customFrom, customTo);
  const navigate = (p: Page) => {
    setPage(p);
    setMenu(false);
  };
  return (
    <div className="app">
      <Sidebar page={page} open={menu} onClose={() => setMenu(false)} onNav={navigate} isAdmin={!!session.isSaasAdmin} />
      <main>
        <header>
          <button className="icon mobile" onClick={() => setMenu(true)} aria-label={t("header.menu")}>
            <Menu />
          </button>
          <div className="org">
            <span className="orgmark">{session.organizationName[0]}</span>
            <label>
              <select value={session.organizationId} aria-label={t("header.organization")} onChange={async(e)=>{const r=await fetch("/api/v1/session",{headers:{"X-Organization-Id":e.target.value}});if(r.ok){setPage("dashboard");setSession(await r.json());}}}>
                {organizations.length?organizations.map(o=><option key={o.id} value={o.id}>{o.name}</option>):<option value={session.organizationId}>{session.organizationName}</option>}
              </select>
              <small>{session.role}</small>
            </label>
            <button className="org-add" title={t("header.createOrganization")} aria-label={t("header.createOrganization")} onClick={async()=>{const name=window.prompt(t("header.organizationPrompt"));if(!name)return;const r=await fetch("/api/v1/organizations",{method:"POST",headers:{"X-Organization-Id":session.organizationId,"Content-Type":"application/json"},body:JSON.stringify({name})});if(!r.ok)return window.alert(t("header.organizationCreateError"));const created=await r.json();const selected=await fetch("/api/v1/session",{headers:{"X-Organization-Id":created.id}});if(selected.ok){setPage("dashboard");setSession(await selected.json());}}}>+</button>
          </div>
          <div className="head-actions">
            <select className="language-select" aria-label="Language" value={locale} onChange={e=>setLocale(e.target.value as "ru"|"kk")}><option value="ru">RU</option><option value="kk">ҚАЗ</option></select>
            <button className="icon">
              <Search />
            </button>
            <button
              className="avatar"
              title={`${session.email} — ${t("common.logout")}`}
              onClick={async () => {
                await fetch("/api/v1/auth/logout", { method: "POST" });
                setSession(null);
              }}
            >
              {(session.displayName || session.email).slice(0, 2).toUpperCase()}
            </button>
          </div>
        </header>
        {page === "dashboard" && <Dashboard summary={summary} products={products} points={points} problems={dashboardProblems} loading={loading} period={period} onPeriod={setPeriod} customFrom={customFrom} customTo={customTo} onCustomFrom={setCustomFrom} onCustomTo={setCustomTo} completeCostsOnly={completeCostsOnly} onCompleteCostsOnly={setCompleteCostsOnly} onNavigate={navigate} onProduct={(id)=>{setFocusedProductId(id);navigate("products")}} />}
        {page === "products" && <Products products={products} session={session} dateFrom={activeRange?.from} dateTo={activeRange?.to} openProductId={focusedProductId} />}
        {page === "orders" && <OrdersPage initialOrders={orders} session={session} products={products} initialDateFrom={activeRange?.from} initialDateTo={activeRange?.to} />}
        {page === "abc" && <Abc session={session} completeCostsOnly={completeCostsOnly} dateFrom={activeRange?.from} dateTo={activeRange?.to} />}
        {page === "integrations" && <KaspiConnections session={session} />}
        {page === "expenses" && <Expenses session={session} products={products} orders={orders} />}
        {page === "fees" && <Fees session={session} products={products} />}
        {page === "exports" && <Exports session={session} dateFrom={activeRange?.from} dateTo={activeRange?.to} completeCostsOnly={completeCostsOnly} />}
        {page === "settings" && <SettingsPage session={session} onSession={setSession} onDeleted={() => setSession(null)} />}
        {page === "admin" && session.isSaasAdmin && <AdminConsole session={session} />}
      </main>
    </div>
  );
}

function periodRange(period: string, customFrom: string, customTo: string) {
  const iso = (date: Date) => date.toISOString().slice(0, 10);
  const today = new Date();
  today.setHours(12, 0, 0, 0);
  let from = new Date(today),
    to = new Date(today);
  if (period === "custom") return customFrom && customTo && customFrom <= customTo ? { from: customFrom, to: customTo } : null;
  if (period === "today") return { from: iso(today), to: iso(today) };
  if (period === "yesterday") {
    from.setDate(from.getDate() - 1);
    return { from: iso(from), to: iso(from) };
  }
  if (period === "month") {
    from = new Date(today.getFullYear(), today.getMonth(), 1, 12);
    return { from: iso(from), to: iso(to) };
  }
  if (period === "previous-month") {
    from = new Date(today.getFullYear(), today.getMonth() - 1, 1, 12);
    to = new Date(today.getFullYear(), today.getMonth(), 0, 12);
    return { from: iso(from), to: iso(to) };
  }
  from.setDate(from.getDate() - Number(period) + 1);
  return { from: iso(from), to: iso(to) };
}

function AuthScreen({ onAuthenticated }: { onAuthenticated: (session: Session) => void }) {
  const {t,locale,setLocale}=useI18n();
  const params = new URLSearchParams(location.search);
  const [register, setRegister] = useState(false),
    [forgot, setForgot] = useState(false),
    [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [success, setSuccess] = useState("");
  if (params.get("resetToken") && params.get("resetEmail")) return <ResetPassword email={params.get("resetEmail")!} token={params.get("resetToken")!} />;
  const submit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setBusy(true);
    setError("");
    const form = new FormData(e.currentTarget);
    const body = register
      ? {
          email: form.get("email"),
          password: form.get("password"),
          displayName: form.get("displayName"),
          organizationName: form.get("organizationName"),
        }
      : { email: form.get("email"), password: form.get("password") };
    try {
      const response = await fetch(`/api/v1/auth/${forgot ? "forgot-password" : register ? "register" : "login"}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.message || data?.title || data?.detail || "Не удалось войти");
      if (forgot) {
        setSuccess(t("auth.forgot.success"));
        return;
      }
      if (data?.emailConfirmationRequired) {
        setSuccess(t("auth.confirm.success"));
        return;
      }
      const sessionResponse = await fetch("/api/v1/session");
      if (!sessionResponse.ok) throw new Error(t("auth.session.error"));
      onAuthenticated(await sessionResponse.json());
    } catch (ex) {
      setError(ex instanceof Error ? ex.message : t("auth.error"));
    } finally {
      setBusy(false);
    }
  };
  return (
    <div className="auth-page">
      <section className="auth-panel">
        <select className="language-select auth-language" aria-label="Language" value={locale} onChange={e=>setLocale(e.target.value as "ru"|"kk")}><option value="ru">RU</option><option value="kk">ҚАЗ</option></select>
        <div className="auth-brand">
          <span>
            <WalletCards />
          </span>
          <div>
            Seller<b>Finance</b>
          </div>
        </div>
        <h1>{forgot ? t("auth.forgot.title") : register ? t("auth.register.title") : t("auth.login.title")}</h1>
        <p>{forgot ? t("auth.forgot.lead") : register ? t("auth.register.lead") : t("auth.login.lead")}</p>
        {success ? (
          <div className="auth-success">{success}</div>
        ) : (
          <form onSubmit={submit}>
            {register && (
              <>
                <label>
                  {t("auth.register.name")}
                  <input name="displayName" required autoComplete="name" />
                </label>
                <label>
                  {t("auth.register.organization")}
                  <input name="organizationName" required />
                </label>
              </>
            )}
            <label>
              {t("auth.email")}
              <input name="email" type="email" required autoComplete="email" />
            </label>
            {!forgot && <label>
              {t("auth.password")}
              <input name="password" type="password" minLength={10} required autoComplete={register ? "new-password" : "current-password"} />
            </label>}
            {error && <div className="auth-error">{error}</div>}
            <button className="primary" disabled={busy}>
              <LogIn />
              {busy ? t("common.wait") : forgot ? t("auth.forgot.submit") : register ? t("auth.register.submit") : t("auth.login.submit")}
            </button>
          </form>
        )}
        {!register && !forgot && <button className="auth-switch" onClick={() => { setForgot(true); setError(""); setSuccess(""); }}>{t("auth.forgot.link")}</button>}
        <button
          className="auth-switch"
          onClick={() => {
            if (forgot) setForgot(false); else setRegister(!register);
            setError("");
            setSuccess("");
          }}
        >
          {forgot ? t("auth.switch.back") : register ? t("auth.switch.login") : t("auth.switch.register")}
        </button>
      </section>
      <aside className="auth-hero">
        <span>{t("auth.hero.eyebrow")}</span>
        <h2>{t("auth.hero.title")}</h2>
        <p>{t("auth.hero.lead")}</p>
      </aside>
    </div>
  );
}
function ResetPassword({ email, token }: { email: string; token: string }) {
  const {t,locale,setLocale}=useI18n();
  const [message, setMessage] = useState("");
  const submit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const password = String(new FormData(e.currentTarget).get("password"));
    const r = await fetch("/api/v1/auth/reset-password", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, token, newPassword: password }),
    });
    setMessage(r.ok ? t("reset.success") : t("reset.error"));
  };
  return (
    <div className="auth-page">
      <section className="auth-panel">
        <select className="language-select auth-language" aria-label="Language" value={locale} onChange={e=>setLocale(e.target.value as "ru"|"kk")}><option value="ru">RU</option><option value="kk">ҚАЗ</option></select>
        <h1>{t("reset.title")}</h1>
        <p>{email}</p>
        <form onSubmit={submit}>
          <label>
            {t("auth.password")}
            <input name="password" type="password" minLength={10} required />
          </label>
          <button className="primary">{t("reset.submit")}</button>
          {message && <div className="auth-success">{message}</div>}
        </form>
      </section>
    </div>
  );
}

function Sidebar({ page, open, onClose, onNav, isAdmin }: { page: Page; open: boolean; onClose: () => void; onNav: (p: Page) => void; isAdmin: boolean }) {
  const {t}=useI18n();
  const nav: [Page, string, React.ReactNode][] = [
    ["dashboard", t("nav.dashboard"), <LayoutDashboard />],
    ["products", t("nav.products"), <Box />],
    ["orders", t("nav.orders"), <ShoppingBag />],
    ["abc", t("nav.abc"), <BarChart3 />],
    ["exports", t("nav.exports"), <Download />],
  ];
  return (
    <>
      <aside className={open ? "open" : ""}>
        <div className="brand">
          <span>
            <WalletCards />
          </span>
          <div>
            Seller<b>Finance</b>
          </div>
          <button className="icon mobile" onClick={onClose}>
            <X />
          </button>
        </div>
        <nav>
          <small>{t("nav.analytics")}</small>
          {nav.map(([id, label, icon]) => (
            <button key={id} className={page === id ? "active" : ""} onClick={() => onNav(id)}>
              {icon}
              {label}
            </button>
          ))}
          <small>{t("nav.management")}</small>
          <button className={page === "expenses" ? "active" : ""} onClick={() => onNav("expenses")}>
            <WalletCards />
            {t("nav.expenses")}
          </button>
          <button className={page === "fees" ? "active" : ""} onClick={() => onNav("fees")}>
            <Settings />
            {t("nav.fees")}
          </button>
          <button className={page === "integrations" ? "active" : ""} onClick={() => onNav("integrations")}>
            <RefreshCw />
            {t("nav.integrations")}
            <span className="dot" />
          </button>
          <button className={page === "settings" ? "active" : ""} onClick={() => onNav("settings")}>
            <Settings />
            {t("nav.settings")}
          </button>
          {isAdmin && (
            <button className={page === "admin" ? "active" : ""} onClick={() => onNav("admin")}>
              <Sparkles />
              {t("nav.admin")}
            </button>
          )}
        </nav>
        <div className="plan">
          <div>
            <Sparkles size={16} />
            {t("plan.trial")}
          </div>
          <b>{t("plan.pilot")}</b>
          <span>
            <i />
          </span>
          <small>{t("plan.organization")}</small>
        </div>
      </aside>
      {open && <div className="shade" onClick={onClose} />}
    </>
  );
}

function Dashboard({ summary, products, points, problems, loading, period, onPeriod, customFrom, customTo, onCustomFrom, onCustomTo, completeCostsOnly, onCompleteCostsOnly, onNavigate, onProduct }: { summary: Summary; products: Product[]; points: Point[]; problems: DashboardProblems; loading: boolean; period: string; onPeriod: (value: string) => void; customFrom: string; customTo: string; onCustomFrom: (value: string) => void; onCustomTo: (value: string) => void; completeCostsOnly: boolean; onCompleteCostsOnly: (value: boolean) => void; onNavigate:(page:Page)=>void; onProduct:(id:string)=>void }) {
  const {t,locale}=useI18n();const localeCode=locale==="kk"?"kk-KZ":"ru-RU";
  const [topMode,setTopMode]=useState<"profit"|"loss">("profit");
  const max = Math.max(1, ...points.flatMap((x) => [Math.abs(x.revenue),Math.abs(x.profit)]));
  const topProfit=products.filter(x=>x.profit!==null&&x.profit>0).sort((a,b)=>(b.profit??0)-(a.profit??0)).slice(0,10);
  const topLoss=products.filter(x=>x.profit!==null&&x.profit<0).sort((a,b)=>(a.profit??0)-(b.profit??0)).slice(0,10);
  return (
    <section className="content">
      <div className="title-row">
        <div>
          <span className="eyebrow">{new Date().toLocaleDateString(localeCode,{day:"numeric",month:"long",year:"numeric"})}</span>
          <h1>{t("dashboard.title")}</h1>
          <p>{t("dashboard.lead")}</p>
        </div>
        <div className="actions">
          <button className="secondary" onClick={()=>onNavigate("exports")}>
            <Download />
            {t("nav.exports")}
          </button>
          <button className="primary" onClick={()=>onNavigate("integrations")}>
            <RefreshCw className={loading ? "spin" : ""} />
            {t("dashboard.sync")}
          </button>
        </div>
      </div>
      <div className="toolbar">
        <select value={period} onChange={(e) => onPeriod(e.target.value)}>
          <option value="today">{t("dashboard.today")}</option>
          <option value="yesterday">{t("dashboard.yesterday")}</option>
          <option value="7">{t("dashboard.days7")}</option>
          <option value="30">{t("dashboard.days30")}</option>
          <option value="90">{t("dashboard.days90")}</option>
          <option value="month">{t("dashboard.currentMonth")}</option>
          <option value="previous-month">{t("dashboard.previousMonth")}</option>
          <option value="custom">{t("dashboard.custom")}</option>
        </select>
        {period === "custom" && (
          <div className="custom-period">
            <input aria-label={t("dashboard.periodFrom")} type="date" value={customFrom} onChange={(e) => onCustomFrom(e.target.value)} />
            <span>—</span>
            <input aria-label={t("dashboard.periodTo")} type="date" min={customFrom} value={customTo} onChange={(e) => onCustomTo(e.target.value)} />
          </div>
        )}
        <label className="complete-cost-filter">
          <input type="checkbox" checked={completeCostsOnly} onChange={(event) => onCompleteCostsOnly(event.target.checked)} />
          {t("dashboard.completeCosts")}
        </label>
        <span className="sync">
          <i /> {t("dashboard.loaded")}
        </span>
      </div>
      {summary.isPreliminary && (
        <div className="notice">
          <CircleAlert />
          <div>
            <b>{t("dashboard.preliminary")}</b>
            <span>{t("dashboard.missingCostPrefix")} {money((summary.revenue * (100 - summary.coveragePct)) / 100)} {t("dashboard.missingCostSuffix")}</span>
          </div>
          <button>{t("dashboard.fillCosts")}</button>
        </div>
      )}
      <div className="kpis">
        <Kpi label={t("dashboard.revenue")} value={money(summary.revenue)} sub={`${summary.orders} ${t("dashboard.orders")}`} />
        <Kpi label={t("dashboard.units")} value={String(summary.units)} />
        <Kpi label="COGS" value={money(summary.cogs)} sub={summary.isPreliminary?t("dashboard.incompleteCoverage"):t("dashboard.fullCost")} warning={summary.isPreliminary} />
        <Kpi label={t("dashboard.grossProfit")} value={money(summary.grossProfit)} sub={summary.isPreliminary?t("dashboard.prelimShort"):undefined} warning={summary.isPreliminary} />
        <Kpi label={t("dashboard.fees")} value={money(summary.marketplaceFees)} />
        <Kpi label={t("dashboard.delivery")} value={money(summary.delivery)} />
        <Kpi label={t("dashboard.periodExpenses")} value={money(summary.operatingExpenses)} sub={t("dashboard.expenseKinds")} />
        <Kpi label={t("dashboard.operatingProfit")} value={money(summary.operatingProfit)} sub={summary.isPreliminary?t("dashboard.prelimShort"):undefined} warning={summary.isPreliminary} />
        <Kpi label={t("dashboard.operatingMargin")} value={pct(summary.operatingMarginPct)} sub={summary.revenue===0?t("dashboard.noRevenue"):undefined} />
      </div>
      <div className="grid">
        <article className="chart-card">
          <div className="card-head">
            <div>
              <h2>{t("dashboard.chartTitle")}</h2><p>{t("dashboard.chartLead")}</p>
            </div>
            <div className="legend">
              <span>
                <i className="revenue" />
                {t("dashboard.revenue")}
              </span>
              <span>
                <i className="profit" />
                {t("dashboard.profit")}
              </span>
            </div>
          </div>
          <div className="chart">
            {points.map((p) => (
              <div className="bar-col" key={p.date}>
                <div className="bars">
                  <i className={`profit-bar ${p.profit < 0 ? "negative" : ""}`} style={{ height: `${(Math.abs(p.profit) / max) * 100}%` }} />
                  <i className="revenue-bar" style={{ height: `${(p.revenue / max) * 100}%` }} />
                </div>
                <small>
                  {new Date(p.date).toLocaleDateString(localeCode, {
                    day: "2-digit",
                    month: "short",
                  })}
                </small>
              </div>
            ))}
          </div>
        </article>
        <article className="coverage">
          <div className="card-head">
            <div>
              <h2>{t("dashboard.dataCompleteness")}</h2><p>{t("dashboard.costCoverage")}</p>
            </div>
          </div>
          <div
            className="ring"
            style={
              {
                "--pct": `${summary.coveragePct * 3.6}deg`,
              } as React.CSSProperties
            }
          >
            <div>
              <b>{pct(summary.coveragePct)}</b>
              <span>{t("dashboard.revenueWord")}</span>
            </div>
          </div>
          <p>
            <i /> {problems.totalCount} {t("dashboard.problemCountSuffix")}
          </p>
          <button onClick={() => onNavigate("products")}>
            {t("dashboard.checkProducts")} <span>→</span>
          </button>
        </article>
      </div>
      <div className="bottom-grid">
        <article className="table-card">
          <div className="card-head">
            <div>
              <h2>{topMode === "profit" ? t("dashboard.topProfit") : t("dashboard.topLoss")}</h2><p>{t("dashboard.periodSelected")}</p>
            </div>
            <div className="top-switch"><button className={topMode==="profit"?"active":""} onClick={()=>setTopMode("profit")}>{t("dashboard.profit")}</button><button className={topMode==="loss"?"active":""} onClick={()=>setTopMode("loss")}>{t("dashboard.loss")}</button><button onClick={()=>onNavigate("products")}>{t("dashboard.all")}</button></div>
          </div>
          <ProductTable products={topMode === "profit" ? topProfit : topLoss} onSelect={(product)=>onProduct(product.id)} />
        </article>
        <article className="problems">
          <div className="card-head">
            <div>
              <h2>{t("dashboard.attention")}</h2><p>{t("dashboard.accuracyBlockers")}</p>
            </div>
            <span>{problems.totalCount}</span>
          </div>
          {problems.missingCosts.map((p) => (
            <div className="problem" key={p.id}>
              <PackageSearch />
              <div>
                <b>{t("dashboard.noCost")}</b>
                <span>
                  {p.name} · {p.sku}
                </span>
              </div>
              <button onClick={()=>onProduct(p.id)}>{t("dashboard.add")}</button>
            </div>
          ))}
          {problems.negativeMargins.map((p) => <div className="problem warning" key={`margin-${p.id}`}><TrendingDown/><div><b>{t("dashboard.negativeMargin")}</b><span>{p.name} · {pct(p.margin)} · {money(p.profit)}</span></div><button onClick={()=>onProduct(p.id)}>{t("dashboard.open")}</button></div>)}
          {problems.syncIssues.map((issue) => <div className="problem sync-warning" key={`sync-${issue.id}`}><RefreshCw/><div><b>{t("dashboard.syncError")}</b><span>{issue.storeName} · {issue.errorCode || t("dashboard.checkRequired")}</span></div><button onClick={()=>onNavigate("integrations")}>{t("dashboard.check")}</button></div>)}
          {!problems.totalCount&&<p className="no-problems">{t("dashboard.noProblems")}</p>}
        </article>
      </div>
    </section>
  );
}

function OrdersPage({ initialOrders, session, products, initialDateFrom, initialDateTo }: { initialOrders: Order[]; session: Session; products: Product[]; initialDateFrom?: string; initialDateTo?: string }) {
  const {t,locale}=useI18n();const localeCode=locale==="kk"?"kk-KZ":"ru-RU";
  const [orders, setOrders] = useState(initialOrders),
    [detail, setDetail] = useState<any>(null),
    [status, setStatus] = useState(""),
    [productId, setProductId] = useState(""),
    [profitFrom, setProfitFrom] = useState(""),
    [profitTo, setProfitTo] = useState(""),
    [dateFrom, setDateFrom] = useState(initialDateFrom ?? ""),
    [dateTo, setDateTo] = useState(initialDateTo ?? ""),
    [search, setSearch] = useState(""),
    [page, setPage] = useState(1),
    [totalPages, setTotalPages] = useState(1),
    [total, setTotal] = useState(initialOrders.length),
    [busy, setBusy] = useState(false);
  const headers = { "X-Organization-Id": session.organizationId };
  const load = async (nextPage = page) => {
    setBusy(true);
    const q = new URLSearchParams({ page: String(nextPage), pageSize: "25" });
    if (status) q.set("status", status);
    if (productId) q.set("productId", productId);
    if (profitFrom) q.set("profitFrom", profitFrom);
    if (profitTo) q.set("profitTo", profitTo);
    if (dateFrom) q.set("dateFrom", dateFrom);
    if (dateTo) q.set("dateTo", dateTo);
    if (search) q.set("search", search);
    const r = await fetch("/api/v1/orders?" + q, { headers });
    if (r.ok) {
      const data = await r.json();
      setOrders(data.items);
      setPage(data.page);
      setTotalPages(Math.max(1, data.totalPages));
      setTotal(data.totalCount);
    }
    setBusy(false);
  };
  useEffect(() => {
    setOrders(initialOrders);
    setTotal(initialOrders.length);
  }, [initialOrders]);
  useEffect(() => { setDateFrom(initialDateFrom ?? ""); setDateTo(initialDateTo ?? ""); }, [initialDateFrom, initialDateTo]);
  const apply = (e: React.FormEvent) => {
    e.preventDefault();
    load(1);
  };
  const open = async (id: string) => {
    const r = await fetch(`/api/v1/orders/${id}`, { headers });
    if (r.ok) setDetail(await r.json());
  };
  return (
    <section className="content">
      <div className="title-row">
        <div>
          <span className="eyebrow">{t("orders.eyebrow")}</span>
          <h1>{t("orders.title")}</h1>
          <p>{t("orders.lead")}</p>
        </div>
      </div>
      <form className="order-filters" onSubmit={apply}>
        <input aria-label={t("orders.search")} placeholder={t("orders.number")} value={search} onChange={(e) => setSearch(e.target.value)} />
        <select aria-label={t("orders.status")} value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">{t("orders.allStatuses")}</option>
          <option value="COMPLETED">Completed</option>
          <option value="RETURNED">Returned</option>
          <option value="CANCELLED">Cancelled</option>
          <option value="PENDING">{t("orders.pending")}</option>
        </select>
        <label className="order-date">{t("orders.from")}<input aria-label={t("orders.dateFrom")} type="date" value={dateFrom} onChange={(e) => setDateFrom(e.target.value)} /></label>
        <label className="order-date">{t("orders.to")}<input aria-label={t("orders.dateTo")} type="date" min={dateFrom} value={dateTo} onChange={(e) => setDateTo(e.target.value)} /></label>
        <select aria-label={t("orders.product")} value={productId} onChange={(e) => setProductId(e.target.value)}>
          <option value="">{t("orders.allProducts")}</option>
          {products.map((p) => (
            <option key={p.id} value={p.id}>
              {p.sku}
            </option>
          ))}
        </select>
        <input aria-label={t("orders.profitFrom")} type="number" placeholder={t("orders.profitFrom")} value={profitFrom} onChange={(e) => setProfitFrom(e.target.value)} />
        <input aria-label={t("orders.profitTo")} type="number" placeholder={t("orders.profitTo")} value={profitTo} onChange={(e) => setProfitTo(e.target.value)} />
        <button className="primary" disabled={busy}>
          {busy ? t("orders.loading") : t("orders.apply")}
        </button>
      </form>
      <article className="table-card standalone">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t("orders.order")}</th><th>{t("orders.date")}</th><th>{t("orders.status")}</th><th>{t("orders.amount")}</th><th>{t("orders.fee")}</th><th>{t("orders.delivery")}</th><th>{t("orders.profit")}</th><th>{t("orders.calculation")}</th>
              </tr>
            </thead>
            <tbody>
              {orders.map((o) => (
                <tr key={o.id} onClick={() => open(o.id)}>
                  <td>{o.externalId || o.id}</td>
                  <td>{new Date(o.date).toLocaleDateString(localeCode)}</td>
                  <td>{o.status}</td>
                  <td>{money(o.amount)}</td>
                  <td>{money(o.fees ?? 0)}</td>
                  <td>{money(o.delivery ?? 0)}</td>
                  <td>{money(o.profit ?? null)}</td>
                  <td>
                    <span className={o.complete ? "pill" : "pill missing"}>{o.complete ? t("orders.complete") : t("orders.needsCost")}</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {orders.length === 0 && <div className="empty-row">{t("orders.empty")}</div>}
        </div>
        <div className="pagination">
          <span>{t("orders.found")}: {total}</span>
          <button disabled={page <= 1 || busy} onClick={() => load(page - 1)}>
            ←
          </button>
          <b>
            {page} / {totalPages}
          </b>
          <button disabled={page >= totalPages || busy} onClick={() => load(page + 1)}>
            →
          </button>
        </div>
      </article>
      {detail && (
        <div className="modal-shade" onClick={() => setDetail(null)}>
          <article className="product-modal order-modal" onClick={(e) => e.stopPropagation()}>
            <button className="modal-close" onClick={() => setDetail(null)}>
              <X />
            </button>
            <span className="eyebrow">{t("orders.order").toUpperCase()} {detail.externalId}</span>
            <h2>
              {money(detail.revenue)} · {detail.status}
            </h2>
            <p className="fallback-note">
              {t("orders.code")}: {detail.code || detail.externalId} · {t("orders.payment")}: {detail.paymentMode || t("orders.unspecified")} · {t("orders.completed")}: {detail.completionDate ? new Date(detail.completionDate).toLocaleDateString(localeCode) : t("orders.dateMissing")}
            </p>
            {detail.calculationDateFallback && <p className="fallback-note">{t("orders.fallback")}</p>}
            {detail.statusHistory?.[0]?.externalStatus === "KASPI_DELIVERY_RETURN_REQUESTED" && <p className="fallback-note">{t("orders.returnRequested")}</p>}
            <div className="breakdown">
              <div>
                <span>{t("orders.revenue")}</span>
                <b>{money(detail.revenue)}</b>
              </div>
              <div>
                <span>COGS</span>
                <b>{money(detail.cogs)}</b>
              </div>
              <div>
                <span>{t("orders.fees")}</span>
                <b>{money(detail.marketplaceFees)}</b>
              </div>
              <div>
                <span>{t("orders.delivery")}</span>
                <b>{money(detail.delivery)}</b>
              </div>
              <div>
                <span>{t("orders.variableCosts")}</span>
                <b>{money((detail.variableCosts ?? 0) - (detail.marketplaceFees ?? 0) - (detail.delivery ?? 0))}</b>
              </div>
              <div>
                <span>{t("orders.operatingProfit")}</span>
                <b>{money(detail.operatingProfit)}</b>
              </div>
              <div><span>{t("orders.margin")}</span><b>{pct(detail.operatingMarginPct)}</b></div>
              <div><span>Coverage</span><b>{pct(detail.coveragePct)}</b></div>
            </div>
            <h3>{t("orders.statusHistory")}</h3>
            <div className="status-history">
              {detail.statusHistory?.length ? detail.statusHistory.map((item:any,index:number)=><div key={`${item.changedAt}-${index}`}><b>{item.externalStatus || item.status}</b><span>{new Date(item.changedAt).toLocaleString(localeCode)}</span></div>) : <p className="muted">{t("orders.historyEmpty")}</p>}
            </div>
            <h3>{t("orders.lines")}</h3>
            {detail.lines?.map((l: any) => (
              <div className="order-line" key={l.id}>
                <div>
                  <b>{l.name || l.productId}</b>
                  <span>
                    {l.sku || t("orders.skuUnmapped")} · {l.quantity} {t("orders.pieces")}
                  </span>
                </div>
                <div>
                  <b>{money(l.profit)}</b>
                  <span>
                    {t("orders.lineRevenue")} {money(l.revenue)} · {t("orders.lineCost")} {l.unitCost === null ? t("orders.unspecified") : `${money(l.unitCost)} × ${l.quantity}`} · {t("orders.lineFee")} {money(l.fee)} · {t("orders.lineDelivery")} {money(l.delivery)} · {t("orders.lineOther")} {money(l.otherVariableCosts)}
                  </span>
                </div>
              </div>
            ))}
          </article>
        </div>
      )}
    </section>
  );
}

function Kpi({ label, value, delta, sub, good, warning }: { label: string; value: string; delta?: string; sub?: string; good?: boolean; warning?:boolean }) {
  return (
    <article className={`kpi ${warning?"warning":""}`}>
      <span>{label}</span>
      <b>{value}</b>
      <div className={good ? "good" : ""}>
        {good ? <TrendingUp /> : delta ? <TrendingDown /> : null}
        {delta && <strong>{delta}</strong>}
        {sub && <small>{sub}</small>}
        {delta && <small>к прошлой неделе</small>}
      </div>
    </article>
  );
}
function Orders({ orders, session }: { orders: Order[]; session: Session }) {
  const [detail, setDetail] = useState<any>(null);
  const open = async (id: string) => {
    const r = await fetch(`/api/v1/orders/${id}`, {
      headers: { "X-Organization-Id": session.organizationId },
    });
    if (r.ok) setDetail(await r.json());
  };
  return (
    <section className="content">
      <div className="title-row">
        <div>
          <span className="eyebrow">ПРОДАЖИ</span>
          <h1>Заказы</h1>
          <p>Заказы Kaspi и декомпозиция финансового результата.</p>
        </div>
      </div>
      <article className="table-card standalone">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Заказ</th>
                <th>Дата</th>
                <th>Статус</th>
                <th>Сумма</th>
                <th>Комиссия</th>
                <th>Прибыль</th>
                <th>Расчёт</th>
              </tr>
            </thead>
            <tbody>
              {orders.map((o) => (
                <tr key={o.id} onClick={() => open(o.id)}>
                  <td>{o.externalId || o.id}</td>
                  <td>{new Date(o.date).toLocaleDateString("ru-RU")}</td>
                  <td>{o.status}</td>
                  <td>{money(o.amount)}</td>
                  <td>{money(o.fees ?? 0)}</td>
                  <td>{money(o.profit ?? null)}</td>
                  <td>
                    <span className={o.complete ? "pill" : "pill missing"}>{o.complete ? "Полный" : "Нужна себестоимость"}</span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {orders.length === 0 && <div className="empty-row">Заказов пока нет. Подключите Kaspi и выполните синхронизацию.</div>}
        </div>
      </article>
      {detail && (
        <div className="modal-shade" onClick={() => setDetail(null)}>
          <article className="product-modal order-modal" onClick={(e) => e.stopPropagation()}>
            <button className="modal-close" onClick={() => setDetail(null)}>
              <X />
            </button>
            <span className="eyebrow">ЗАКАЗ {detail.externalId}</span>
            <h2>
              {money(detail.revenue)} · {detail.status}
            </h2>
            {detail.calculationDateFallback && <p className="fallback-note">Дата завершения отсутствует — расчёт выполнен по дате создания.</p>}
            <div className="breakdown">
              <div>
                <span>Выручка</span>
                <b>{money(detail.revenue)}</b>
              </div>
              <div>
                <span>COGS</span>
                <b>{money(detail.cogs)}</b>
              </div>
              <div>
                <span>Комиссии</span>
                <b>{money(detail.marketplaceFees)}</b>
              </div>
              <div>
                <span>Доставка</span>
                <b>{money(detail.delivery)}</b>
              </div>
              <div>
                <span>Операционная прибыль</span>
                <b>{money(detail.operatingProfit)}</b>
              </div>
            </div>
            <h3>Товарные строки</h3>
            {detail.lines?.map((l: any) => (
              <div className="order-line" key={l.id}>
                <div>
                  <b>{l.name || l.productId}</b>
                  <span>
                    {l.sku || "SKU не сопоставлен"} · {l.quantity} шт.
                  </span>
                </div>
                <div>
                  <b>{money(l.profit)}</b>
                  <span>
                    выручка {money(l.revenue)} · комиссия {money(l.fee)}
                  </span>
                </div>
              </div>
            ))}
          </article>
        </div>
      )}
    </section>
  );
}

function Integrations({ session }: { session: Session }) {
  const [state, setState] = useState<any>(null),
    [busy, setBusy] = useState(false),
    [message, setMessage] = useState("");
  const headers = { "X-Organization-Id": session.organizationId };
  const load = () =>
    fetch("/api/v1/kaspi/connection", { headers })
      .then((r) => r.json())
      .then(setState);
  useEffect(() => {
    load();
  }, []);
  const connect = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setBusy(true);
    setMessage("");
    const token = String(new FormData(e.currentTarget).get("token") || "");
    const r = await fetch("/api/v1/kaspi/connection", {
      method: "POST",
      headers: { ...headers, "Content-Type": "application/json" },
      body: JSON.stringify({ token }),
    });
    setMessage(r.ok ? "Kaspi успешно подключён." : (await r.json().catch(() => null))?.detail || "Не удалось проверить токен.");
    setBusy(false);
    if (r.ok) load();
  };
  const sync = async () => {
    setBusy(true);
    const r = await fetch("/api/v1/kaspi/sync", { method: "POST", headers });
    setMessage(r.ok ? "Синхронизация поставлена в очередь." : "Не удалось запустить синхронизацию.");
    setBusy(false);
    setTimeout(load, 1500);
  };
  return (
    <section className="content">
      <div className="title-row">
        <div>
          <span className="eyebrow">ИНТЕГРАЦИИ</span>
          <h1>Kaspi Магазин</h1>
          <p>Токен шифруется AES-256-GCM и никогда не отображается после сохранения.</p>
        </div>
      </div>
      <article className="integration-card">
        <div className="integration-status">
          <span className="orgmark">K</span>
          <div>
            <b>Kaspi Магазин</b>
            <small>{state?.connected ? `Статус: ${state.status}` : "Не подключён"}</small>
          </div>
        </div>
        {state?.connected ? (
          <>
            <dl>
              <div>
                <dt>Последняя проверка</dt>
                <dd>{state.lastVerifiedAt ? new Date(state.lastVerifiedAt).toLocaleString("ru-RU") : "—"}</dd>
              </div>
              <div>
                <dt>Последняя синхронизация</dt>
                <dd>{state.lastSuccessfulSyncAt ? new Date(state.lastSuccessfulSyncAt).toLocaleString("ru-RU") : "—"}</dd>
              </div>
              <div>
                <dt>Задание</dt>
                <dd>
                  {state.lastJob?.status || "—"} {state.lastJob?.importedOrders ? `· ${state.lastJob.importedOrders} заказов` : ""}
                </dd>
              </div>
            </dl>
            <button className="primary" onClick={sync} disabled={busy}>
              <RefreshCw className={busy ? "spin" : ""} />
              Синхронизировать 30 дней
            </button>
          </>
        ) : (
          <form className="token-form" onSubmit={connect}>
            <label>
              API-токен Kaspi
              <input name="token" type="password" required autoComplete="off" placeholder="Вставьте токен из кабинета Kaspi" />
            </label>
            <button className="primary" disabled={busy}>
              {busy ? "Проверяем…" : "Проверить и подключить"}
            </button>
          </form>
        )}
        {message && <p className="integration-message">{message}</p>}
      </article>
    </section>
  );
}

function Abc({ session, completeCostsOnly, dateFrom, dateTo }: { session: Session; completeCostsOnly: boolean; dateFrom?: string; dateTo?: string }) {
  const [metric, setMetric] = useState("profit"),
    [rows, setRows] = useState<any[]>([]);
  useEffect(() => {
    const query = new URLSearchParams({ metric, completeCostsOnly: String(completeCostsOnly) });
    if (dateFrom) query.set("dateFrom", dateFrom);
    if (dateTo) query.set("dateTo", dateTo);
    fetch(`/api/v1/analytics/abc?${query}`, {
      headers: { "X-Organization-Id": session.organizationId },
    })
      .then((r) => r.json())
      .then(setRows);
  }, [metric, completeCostsOnly, dateFrom, dateTo]);
  return (
    <section className="content">
      <div className="title-row">
        <div>
          <span className="eyebrow">АССОРТИМЕНТ</span>
          <h1>ABC-анализ</h1>
          <p>Группы A/B/C по накопительному вкладу 80/15/5.</p>
        </div>
        <select value={metric} onChange={(e) => setMetric(e.target.value)}>
          <option value="profit">Операционная прибыль</option>
          <option value="grossProfit">Валовая прибыль</option>
          <option value="revenue">Выручка</option>
          <option value="units">Количество</option>
        </select>
      </div>
      <article className="table-card standalone">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Группа</th>
                <th>Товар</th>
                <th>Значение</th>
                <th>Выручка</th>
                <th>Расходы</th>
                <th>Прибыль</th>
                <th>Накопительно</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.productId}>
                  <td>
                    <span className={`abc-badge abc-${r.group.toLowerCase()}`}>{r.group}</span>
                  </td>
                  <td>
                    <div>
                      <b>{r.name}</b>
                      <small>{r.sku}</small>
                    </div>
                  </td>
                  <td>{money(r.value)}</td>
                  <td>{money(r.revenue)}</td>
                  <td>{money(r.profit)}</td>
                  <td>{pct(r.cumulativePct)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </article>
    </section>
  );
}

function FinancialImporter({ session, type, onApplied }: { session: Session; type: "Expenses" | "ActualFees"; onApplied?: () => void }) {
  const { t } = useI18n();
  const [preview, setPreview] = useState<any>(null);
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const headers = { "X-Organization-Id": session.organizationId };
  const upload = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) return;
    setBusy(true);
    setMessage(t("financial.checkingFile"));
    const form = new FormData();
    form.append("file", file);
    const response = await fetch(`/api/v1/financial-imports/${type}/preview`, {
      method: "POST",
      headers,
      body: form,
    });
    const data = await response.json().catch(() => null);
    if (response.ok) {
      setPreview(data);
      setMessage("");
    } else setMessage(data?.title || t("financial.readError"));
    setBusy(false);
    event.target.value = "";
  };
  const confirm = async () => {
    if (!preview) return;
    setBusy(true);
    const response = await fetch(`/api/v1/financial-imports/${preview.id}/confirm`, { method: "POST", headers });
    const data = await response.json().catch(() => null);
    if (response.ok) {
      setMessage(`${t("financial.appliedRows")}: ${data.appliedRows}`);
      setPreview(null);
      onApplied?.();
    } else setMessage(data?.title || t("financial.applyError"));
    setBusy(false);
  };
  return (
    <article className="import-preview financial-import">
      <div>
        <h2>{type === "Expenses" ? t("financial.expensesImport") : t("financial.feesImport")}</h2>
        <p>{t("financial.importLead")}</p>
        <a href={`/api/v1/financial-imports/${type}/template.xlsx`}>{t("financial.downloadTemplate")}</a>
      </div>
      <label className="primary file-button">
        {busy ? t("financial.checking") : t("financial.chooseFile")}
        <input type="file" accept=".csv,.xlsx" onChange={upload} disabled={busy} />
      </label>
      {message && <p className="integration-message">{message}</p>}
      {preview && (
        <>
          <p>
            {t("financial.rows")}: {preview.totalRows} · {t("financial.newRows")}: {preview.validRows} · {t("financial.updates")}: {preview.updateRows} · {t("financial.duplicates")}: {preview.duplicateRows} · {t("financial.errors")}: {preview.errorRows}
          </p>
          <button className="primary" onClick={confirm} disabled={busy || !preview.expectedChanges}>
            {t("financial.apply")} {preview.expectedChanges} {t("financial.changes")}
          </button>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>{t("financial.row")}</th>
                  <th>{t("financial.amount")}</th>
                  <th>{t("financial.dateLink")}</th>
                  <th>{t("financial.status")}</th>
                </tr>
              </thead>
              <tbody>
                {preview.rows.map((row: any) => (
                  <tr key={row.rowNumber}>
                    <td>{row.rowNumber}</td>
                    <td>{money(row.amount)}</td>
                    <td>{row.date || row.orderLineId || "—"}</td>
                    <td>
                      <span className={row.status === "Valid" || row.status === "Update" ? "pill" : "pill missing"}>{row.error || row.status}</span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </article>
  );
}

function Expenses({ session, products, orders }: { session: Session; products: Product[]; orders: Order[] }) {
  const { t, locale } = useI18n();
  const localeCode = locale === "kk" ? "kk-KZ" : "ru-RU";
  const expenseTypeLabel = (type: string) => ({ Advertising: t("expenses.advertising"), Packaging: t("expenses.packaging"), Fulfillment: t("expenses.fulfillment"), Services: t("expenses.services"), Other: t("expenses.other") }[type] || type);
  const [rows, setRows] = useState<any[]>([]),
    [message, setMessage] = useState("");
  const headers = { "X-Organization-Id": session.organizationId };
  const load = () =>
    fetch("/api/v1/expenses", { headers })
      .then((r) => r.json())
      .then(setRows);
  useEffect(() => {
    load();
  }, []);
  const submit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    const r = await fetch("/api/v1/expenses", {
      method: "POST",
      headers: { ...headers, "Content-Type": "application/json" },
      body: JSON.stringify({
        type: f.get("type"),
        amount: Number(f.get("amount")),
        date: f.get("date"),
        periodEnd: f.get("periodEnd") || null,
        productId: f.get("productId") || null,
        orderId: f.get("orderId") || null,
        comment: f.get("comment"),
      }),
    });
    setMessage(r.ok ? t("expenses.added") : t("expenses.addError"));
    if (r.ok) {
      e.currentTarget.reset();
      load();
    }
  };
  const remove = async (id: string) => {
    await fetch(`/api/v1/expenses/${id}`, { method: "DELETE", headers });
    load();
  };
  return (
    <section className="content">
      <div className="title-row">
        <div>
          <span className="eyebrow">{t("expenses.eyebrow")}</span>
          <h1>{t("expenses.title")}</h1>
          <p>{t("expenses.lead")}</p>
        </div>
      </div>
      <article className="entry-card">
        <form className="expense-form" onSubmit={submit}>
          <select name="type">
            <option value="Advertising">{t("expenses.advertising")}</option>
            <option value="Packaging">{t("expenses.packaging")}</option>
            <option value="Fulfillment">{t("expenses.fulfillment")}</option>
            <option value="Services">{t("expenses.services")}</option>
            <option value="Other">{t("expenses.other")}</option>
          </select>
          <input name="amount" type="number" min="0.01" step="0.01" placeholder={t("expenses.amountPlaceholder")} required />
          <input name="date" type="date" defaultValue={new Date().toISOString().slice(0, 10)} required />
          <input name="periodEnd" type="date" aria-label={t("expenses.periodEnd")} title={t("expenses.periodEndHint")} />
          <select name="productId">
            <option value="">{t("expenses.organizationAll")}</option>
            {products.map((p) => (
              <option value={p.id} key={p.id}>
                {p.sku}
              </option>
            ))}
          </select>
          <select name="orderId" aria-label={t("expenses.order")}>
            <option value="">{t("expenses.noOrder")}</option>
            {orders.map((order)=><option value={order.id} key={order.id}>{t("expenses.order")} {order.externalId||order.id}</option>)}
          </select>
          <input name="comment" placeholder={t("expenses.comment")} />
          <button className="primary">{t("expenses.add")}</button>
        </form>
        {message && <p className="integration-message">{message}</p>}
      </article>
      <FinancialImporter session={session} type="Expenses" onApplied={load} />
      <article className="table-card standalone">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t("expenses.datePeriod")}</th>
                <th>{t("expenses.type")}</th>
                <th>{t("financial.amount")}</th>
                <th>{t("expenses.link")}</th>
                <th>{t("expenses.comment")}</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id}>
                  <td>{new Date(r.date).toLocaleDateString(localeCode)}{r.periodEnd&&r.periodEnd!==r.date?` — ${new Date(r.periodEnd).toLocaleDateString(localeCode)}`:""}</td>
                  <td>{expenseTypeLabel(r.type)}</td>
                  <td>{money(r.amount)}</td>
                  <td>{r.orderId?`${t("expenses.order")} ${orders.find((order)=>order.id===r.orderId)?.externalId||r.orderId}`:r.productId ? products.find((p) => p.id === r.productId)?.sku : t("expenses.organization")}</td>
                  <td>{r.comment || "—"}</td>
                  <td>
                    <button className="text-danger" onClick={() => remove(r.id)}>
                      {t("expenses.delete")}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </article>
    </section>
  );
}

function Fees({ session, products }: { session: Session; products: Product[] }) {
  const { t, locale } = useI18n();
  const localeCode = locale === "kk" ? "kk-KZ" : "ru-RU";
  const [rows, setRows] = useState<any[]>([]),
    [message, setMessage] = useState(""),
    [scope, setScope] = useState("Default"),
    [endDates, setEndDates] = useState<Record<string, string>>({});
  const categories = [...new Set(products.map((p) => p.category?.trim()).filter((x): x is string => Boolean(x)))].sort();
  const headers = { "X-Organization-Id": session.organizationId };
  const load = () =>
    fetch("/api/v1/fee-rules", { headers })
      .then((r) => r.json())
      .then(setRows);
  useEffect(() => {
    load();
  }, []);
  const submit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    const scope = String(f.get("scope"));
    const r = await fetch("/api/v1/fee-rules", {
      method: "POST",
      headers: { ...headers, "Content-Type": "application/json" },
      body: JSON.stringify({
        scope,
        valueType: f.get("valueType"),
        value: Number(f.get("value")),
        effectiveFrom: f.get("date"),
        effectiveTo: f.get("effectiveTo") || null,
        productId: scope === "Product" ? f.get("productId") : null,
        category: scope === "Category" ? f.get("category") : null,
      }),
    });
    setMessage(r.ok ? t("fees.created") : t("fees.checkParams"));
    if (r.ok) load();
  };
  const endRule = async (id: string) => {
    const effectiveTo = endDates[id];
    if (!effectiveTo) return setMessage(t("fees.endDateRequired"));
    const r = await fetch(`/api/v1/fee-rules/${id}/end`, {
      method: "PUT",
      headers: { ...headers, "Content-Type": "application/json" },
      body: JSON.stringify({ effectiveTo }),
    });
    setMessage(r.ok ? t("fees.updated") : t("fees.endError"));
    if (r.ok) load();
  };
  return (
    <section className="content">
      <div className="title-row">
        <div>
          <span className="eyebrow">{t("fees.eyebrow")}</span>
          <h1>{t("fees.title")}</h1>
          <p>{t("fees.lead")}</p>
        </div>
      </div>
      <article className="entry-card">
        <form className="expense-form" onSubmit={submit}>
          <select name="scope" value={scope} onChange={(e) => setScope(e.target.value)}>
            <option value="Default">{t("fees.scopeOrganization")}</option>
            <option value="Category">{t("fees.scopeCategory")}</option>
            <option value="Product">{t("fees.scopeProduct")}</option>
          </select>
          {scope === "Product" && <select name="productId" required>
            {products.map((p) => (
              <option value={p.id} key={p.id}>
                {p.sku}
              </option>
            ))}
          </select>}
          {scope === "Category" && <select name="category" required>
            <option value="">{t("fees.chooseCategory")}</option>
            {categories.map((category) => <option value={category} key={category}>{category}</option>)}
          </select>}
          <select name="valueType">
            <option value="Percentage">{t("fees.percentage")}</option>
            <option value="Fixed">{t("fees.fixed")}</option>
          </select>
          <input name="value" type="number" min="0" step="0.01" placeholder={t("fees.value")} required />
          <input name="date" type="date" defaultValue={new Date().toISOString().slice(0, 10)} required />
          <input name="effectiveTo" type="date" aria-label={t("fees.effectiveTo")} />
          <button className="primary">{t("fees.create")}</button>
        </form>
        {message && <p className="integration-message">{message}</p>}
      </article>
      <FinancialImporter session={session} type="ActualFees" />
      <article className="table-card standalone">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t("fees.scope")}</th>
                <th>{t("fees.assignment")}</th>
                <th>{t("fees.type")}</th>
                <th>{t("fees.value")}</th>
                <th>{t("fees.effectiveFrom")}</th>
                <th>{t("fees.effectiveTo")}</th>
                <th>{t("fees.finish")}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id}>
                  <td>{r.scope === "Product" ? t("fees.scopeProduct") : r.scope === "Category" ? t("fees.scopeCategory") : t("fees.scopeOrganization")}</td>
                  <td>{r.scope === "Product" ? products.find((p) => p.id === r.productId)?.sku || t("fees.productDeleted") : r.scope === "Category" ? r.category : t("fees.allProducts")}</td>
                  <td>{r.valueType === "Percentage" ? t("fees.percentage") : t("fees.fixed")}</td>
                  <td>{r.valueType === "Percentage" ? `${r.value}%` : money(r.value)}</td>
                  <td>{new Date(r.effectiveFrom).toLocaleDateString(localeCode)}</td>
                  <td>{r.effectiveTo ? new Date(r.effectiveTo).toLocaleDateString(localeCode) : t("fees.unlimited")}</td>
                  <td>
                    <div className="rule-end">
                      <input type="date" min={r.effectiveFrom} value={endDates[r.id] || ""} onChange={(e) => setEndDates((x) => ({ ...x, [r.id]: e.target.value }))} />
                      <button type="button" onClick={() => endRule(r.id)}>{t("fees.finish")}</button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </article>
    </section>
  );
}

function Exports({ session, dateFrom, dateTo, completeCostsOnly }: { session: Session; dateFrom?:string; dateTo?:string; completeCostsOnly:boolean }) {
  const [jobs, setJobs] = useState<any[]>([]),
    [busy, setBusy] = useState(false);
  const headers = { "X-Organization-Id": session.organizationId };
  const create = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setBusy(true);
    const f = new FormData(e.currentTarget);
    const r = await fetch("/api/v1/exports", {
      method: "POST",
      headers: { ...headers, "Content-Type": "application/json" },
      body: JSON.stringify({
        reportType: f.get("report"),
        format: f.get("format"),
        dateFrom: f.get("dateFrom") || null,
        dateTo: f.get("dateTo") || null,
        completeCostsOnly: f.get("completeCostsOnly") === "on",
      }),
    });
    if (r.ok) {
      const created = await r.json();
      const item = {
        ...created,
        report: f.get("report"),
        format: f.get("format"),
      };
      setJobs((x) => [item, ...x]);
      poll(item);
    }
    setBusy(false);
  };
  const poll = async (job: any) => {
    for (let i = 0; i < 30; i++) {
      await new Promise((r) => setTimeout(r, 2000));
      const response = await fetch(`/api/v1/exports/${job.id}`, { headers });
      if (!response.ok) return;
      const state = await response.json();
      setJobs((items) => items.map((x) => (x.id === job.id ? { ...x, ...state } : x)));
      if (state.status === "Succeeded" || state.status === "Failed") return;
    }
  };
  return (
    <section className="content">
      <div className="title-row">
        <div>
          <span className="eyebrow">ОТЧЁТЫ</span>
          <h1>Экспорт</h1>
          <p>Выгрузки создаются в фоне; ссылка действует один час.</p>
        </div>
      </div>
      <article className="entry-card">
        <form className="export-form" onSubmit={create}>
          <select name="report">
            <option value="Products">Прибыль по товарам</option>
            <option value="Orders">Заказы</option>
            <option value="MissingCosts">Товары без себестоимости</option>
            <option value="Abc">ABC-анализ</option>
          </select>
          <select name="format">
            <option value="xlsx">XLSX</option>
            <option value="csv">CSV UTF-8</option>
          </select>
          <label className="export-date">С<input name="dateFrom" type="date" defaultValue={dateFrom}/></label>
          <label className="export-date">По<input name="dateTo" type="date" min={dateFrom} defaultValue={dateTo}/></label>
          <label className="export-complete"><input name="completeCostsOnly" type="checkbox" defaultChecked={completeCostsOnly}/>Только продажи с полной себестоимостью</label>
          <button className="primary" disabled={busy}>
            {busy ? "Создаём…" : "Сформировать"}
          </button>
        </form>
      </article>
      <div className="export-jobs">
        {jobs.map((j) => (
          <article key={j.id}>
            <div>
              <b>
                {j.report} · {String(j.format).toUpperCase()}
              </b>
              <span>
                {j.status}
                {j.rowCount ? ` · ${j.rowCount} строк` : ""}
              </span>
            </div>
            {j.status === "Succeeded" && (
              <a className="primary" href={`/api/v1/exports/download/${j.downloadToken}`}>
                Скачать
              </a>
            )}
          </article>
        ))}
      </div>
    </section>
  );
}

function SettingsPage({ session, onSession, onDeleted }: { session: Session; onSession: (session: Session) => void; onDeleted: () => void }) {
  const [telegram, setTelegram] = useState<any>(null),
    [link, setLink] = useState<any>(null),
    [message, setMessage] = useState(""),
    [members, setMembers] = useState<any[]>([]),
    [invitations, setInvitations] = useState<any[]>([]),
    [invitationLink, setInvitationLink] = useState(""),
    [deleting, setDeleting] = useState(false);
  const headers = { "X-Organization-Id": session.organizationId };
  const load = () =>
    fetch("/api/v1/telegram", { headers })
      .then((r) => r.json())
      .then(setTelegram);
  const loadMembers = async () => {
    const memberResponse = await fetch(`/api/v1/organizations/${session.organizationId}/members`, { headers });
    if (memberResponse.ok) setMembers(await memberResponse.json());
    if (session.role === "Owner" || session.role === "Admin") {
      const invitationResponse = await fetch(`/api/v1/organizations/${session.organizationId}/invitations`, { headers });
      if (invitationResponse.ok) setInvitations(await invitationResponse.json());
    }
  };
  useEffect(() => {
    load();
    loadMembers();
  }, []);
  const inviteMember = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();const formElement=event.currentTarget;const form=new FormData(formElement);const response=await fetch(`/api/v1/organizations/${session.organizationId}/members`,{method:"POST",headers:{...headers,"Content-Type":"application/json"},body:JSON.stringify({email:form.get("email"),role:form.get("role")})});const data=await response.json().catch(()=>null);
    if(response.ok){setMessage(data.delivered?"Приглашение отправлено по email":"Приглашение создано — передайте ссылку пользователю");setInvitationLink(data.invitationUrl);formElement.reset();loadMembers();}else setMessage(data?.title||data?.detail||"Не удалось создать приглашение");
  };
  const changeMemberRole = async (userId:string,role:string) => {const response=await fetch(`/api/v1/organizations/${session.organizationId}/members/${userId}/role`,{method:"PUT",headers:{...headers,"Content-Type":"application/json"},body:JSON.stringify({role})});setMessage(response.ok?"Роль изменена":"Не удалось изменить роль");if(response.ok)loadMembers();};
  const removeMember = async (userId:string) => {if(!window.confirm("Удалить пользователя из организации?"))return;const response=await fetch(`/api/v1/organizations/${session.organizationId}/members/${userId}`,{method:"DELETE",headers});setMessage(response.ok?"Участник удалён":"Не удалось удалить участника");if(response.ok)loadMembers();};
  const cancelInvitation = async (id:string) => {const response=await fetch(`/api/v1/organizations/${session.organizationId}/invitations/${id}`,{method:"DELETE",headers});setMessage(response.ok?"Приглашение отменено":"Не удалось отменить приглашение");if(response.ok)loadMembers();};
  const startLink = async () => {
    const r = await fetch("/api/v1/telegram/link", { method: "POST", headers });
    const data = await r.json();
    if (r.ok) setLink(data);
    else setMessage(data.detail || "Telegram bot пока не настроен");
  };
  const test = async () => {
    const r = await fetch("/api/v1/telegram/test", { method: "POST", headers });
    setMessage(r.ok ? "Тест отправлен" : "Не удалось отправить тест");
  };
  const toggle = async (type: string, enabled: boolean) => {
    await fetch(`/api/v1/telegram/rules/${type}`, {
      method: "PUT",
      headers: { ...headers, "Content-Type": "application/json" },
      body: JSON.stringify({ enabled, threshold: null }),
    });
    load();
  };
  const saveOrganization = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    const r = await fetch(`/api/v1/organizations/${session.organizationId}`, {
      method: "PUT",
      headers: { ...headers, "Content-Type": "application/json" },
      body: JSON.stringify({
        name: f.get("name"),
        timeZone: f.get("timeZone"),
        currency: "KZT",
        allocateOrganizationExpenses: f.get("allocateOrganizationExpenses") === "on",
      }),
    });
    const data = await r.json().catch(() => null);
    if (r.ok) {
      onSession({
        ...session,
        organizationName: data.name,
        timeZone: data.timeZone,
        currency: data.currency,
        allocateOrganizationExpenses: data.allocateOrganizationExpenses,
      });
      setMessage("Настройки организации сохранены");
    } else setMessage(data?.title || "Не удалось сохранить настройки");
  };
  const deleteOrganization = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    if (f.get("organizationName") !== session.organizationName) {
      setMessage("Введите точное название организации");
      return;
    }
    setDeleting(true);
    const r = await fetch(`/api/v1/organizations/${session.organizationId}`, {
      method: "DELETE",
      headers: { ...headers, "Content-Type": "application/json" },
      body: JSON.stringify({
        organizationName: f.get("organizationName"),
        password: f.get("password"),
      }),
    });
    const data = await r.json().catch(() => null);
    if (r.ok) onDeleted();
    else setMessage(data?.title || data?.detail || "Удаление не выполнено");
    setDeleting(false);
  };
  const rule = (type: string) => telegram?.rules?.find((x: any) => x.eventType === type)?.enabled ?? false;
  const canManage = session.role === "Owner" || session.role === "Admin";
  return (
    <section className="content">
      <div className="title-row">
        <div>
          <span className="eyebrow">ОРГАНИЗАЦИЯ</span>
          <h1>Настройки</h1>
          <p>
            {session.organizationName} · тариф {session.plan}
          </p>
        </div>
      </div>
      {message && <p className="integration-message">{message}</p>}
      <div className="settings-grid">
        <article className="entry-card">
          <h2>Организация</h2>
          <form className="settings-form" onSubmit={saveOrganization}>
            <label>
              Название
              <input name="name" defaultValue={session.organizationName} minLength={2} maxLength={120} required disabled={!canManage} />
            </label>
            <label>
              Часовой пояс
              <select name="timeZone" defaultValue={session.timeZone || "Asia/Almaty"} disabled={!canManage}>
                <option value="Asia/Almaty">Asia/Almaty</option>
                <option value="Asia/Qyzylorda">Asia/Qyzylorda</option>
                <option value="Asia/Aqtobe">Asia/Aqtobe</option>
                <option value="Asia/Aqtau">Asia/Aqtau</option>
                <option value="Asia/Atyrau">Asia/Atyrau</option>
                <option value="Asia/Oral">Asia/Oral</option>
              </select>
            </label>
            <label>
              Валюта
              <input value="KZT" disabled />
            </label>
            <label className="allocation-setting">
              <input name="allocateOrganizationExpenses" type="checkbox" defaultChecked={session.allocateOrganizationExpenses} disabled={!canManage} />
              Распределять общеорганизационные расходы по выручке товаров
              <small>Общий результат не изменится. Настройка влияет только на прибыльность товаров и ABC.</small>
            </label>
            {canManage && <button className="primary">Сохранить</button>}
          </form>
        </article>
        <article className="entry-card">
          <h2>Тариф</h2>
          <b className="plan-name">{session.plan}</b>
          <p>Trial: 2 пользователя · Start: 3 · Pro: 10 · Business: 30.</p>
          {session.trialEndsAt && <small>Trial до {new Date(session.trialEndsAt).toLocaleDateString("ru-RU")}</small>}
        </article>
        <article className="entry-card">
          <h2>Telegram</h2>
          <p>
            Статус: <b>{telegram?.status || "Проверяем…"}</b>
          </p>
          {telegram?.status === "Active" ? (
            <button className="primary" onClick={test}>
              Отправить тест
            </button>
          ) : (
            <button className="primary" onClick={startLink}>
              Связать Telegram
            </button>
          )}
          {link && (
            <div className="telegram-link">
              <p>
                Код действует 15 минут: <b>{link.code}</b>
              </p>
              {link.deepLink && (
                <a className="primary" href={link.deepLink} target="_blank" rel="noreferrer">
                  Открыть Telegram
                </a>
              )}
            </div>
          )}
        </article>
        <article className="entry-card notification-rules">
          <h2>Правила уведомлений</h2>
          {[
            ["MissingCost", "Нет себестоимости"],
            ["NegativeMargin", "Отрицательная маржа"],
            ["SyncRequiresAttention", "Ошибка синхронизации"],
          ].map(([type, label]) => (
            <label key={type}>
              <input type="checkbox" checked={rule(type)} onChange={(e) => toggle(type, e.target.checked)} />
              {label}
            </label>
          ))}
        </article>
      </div>
      <article className="entry-card members-card">
        <h2>Пользователи и роли</h2>
        <div className="member-list">
          {members.map((member:any)=><div className="member-row" key={member.id}><div><b>{member.displayName||member.email}</b><small>{member.email}</small></div><select value={member.role} disabled={!canManage||member.role==="Owner"} onChange={event=>changeMemberRole(member.id,event.target.value)}><option value="Owner">Owner</option><option value="Admin">Admin</option><option value="Analyst">Analyst</option><option value="Viewer">Viewer</option></select>{canManage&&member.role!=="Owner"&&<button className="text-danger" onClick={()=>removeMember(member.id)}>Удалить</button>}</div>)}
        </div>
        {canManage&&<><form className="member-invite-form" onSubmit={inviteMember}><input name="email" type="email" placeholder="user@example.com" required/><select name="role" defaultValue="Analyst"><option value="Admin">Admin</option><option value="Analyst">Analyst</option><option value="Viewer">Viewer</option></select><button className="primary">Пригласить</button></form>{invitationLink&&<label className="invitation-link">Ссылка приглашения<input readOnly value={invitationLink} onFocus={event=>event.currentTarget.select()}/></label>}{invitations.map((invitation:any)=><div className="pending-invitation" key={invitation.id}><span>{invitation.email} · {invitation.role} · до {new Date(invitation.expiresAt).toLocaleDateString("ru-RU")}</span><button className="text-danger" onClick={()=>cancelInvitation(invitation.id)}>Отменить</button></div>)}</>}
      </article>
      {session.role === "Owner" && (
        <article className="entry-card danger-zone">
          <h2>Удаление организации</h2>
          <p>Будут безвозвратно удалены заказы, товары, расходы, выгрузки, интеграции и участники. Если это ваша единственная организация, аккаунт также будет удалён.</p>
          <form className="settings-form" onSubmit={deleteOrganization}>
            <label>
              Введите «{session.organizationName}»
              <input name="organizationName" required autoComplete="off" />
            </label>
            <label>
              Текущий пароль
              <input name="password" type="password" required autoComplete="current-password" />
            </label>
            <button className="danger-button" disabled={deleting}>
              {deleting ? "Удаляем…" : "Удалить организацию и данные"}
            </button>
          </form>
        </article>
      )}
    </section>
  );
}

function AdminPage({ session }: { session: Session }) {
  const [rows, setRows] = useState<any[]>([]);
  const headers = { "X-Organization-Id": session.organizationId };
  const load = () =>
    fetch("/api/v1/admin/organizations", { headers })
      .then((r) => r.json())
      .then(setRows);
  useEffect(() => {
    load();
  }, []);
  const change = async (id: string, plan: string) => {
    await fetch(`/api/v1/admin/organizations/${id}/plan`, {
      method: "PUT",
      headers: { ...headers, "Content-Type": "application/json" },
      body: JSON.stringify({ plan, status: "Active" }),
    });
    load();
  };
  return (
    <section className="content">
      <div className="title-row">
        <div>
          <span className="eyebrow">SAAS ADMIN</span>
          <h1>Организации</h1>
          <p>Тарифы, состояние и последняя синхронизация.</p>
        </div>
      </div>
      <article className="table-card standalone">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Организация</th>
                <th>Тариф</th>
                <th>Статус</th>
                <th>Создана</th>
                <th>Последняя синхронизация</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id}>
                  <td>{r.name}</td>
                  <td>
                    <select value={r.plan} onChange={(e) => change(r.id, e.target.value)}>
                      <option>Trial</option>
                      <option>Start</option>
                      <option>Pro</option>
                      <option>Business</option>
                    </select>
                  </td>
                  <td>{r.status}</td>
                  <td>{new Date(r.createdAt).toLocaleDateString("ru-RU")}</td>
                  <td>{r.lastSync ? new Date(r.lastSync).toLocaleString("ru-RU") : "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </article>
    </section>
  );
}
function ProductTable({ products, onSelect }: { products: Product[]; onSelect?:(product:Product)=>void }) {
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr>
            <th>Товар</th>
            <th>Выручка</th>
            <th>Прибыль</th>
            <th>Маржа</th>
          </tr>
        </thead>
        <tbody>
          {products.map((p) => (
            <tr key={p.id} onClick={()=>onSelect?.(p)}>
              <td>
                <span className="product-icon">{p.name[0]}</span>
                <div>
                  <b>{p.name}</b>
                  <small>{p.sku}</small>
                </div>
              </td>
              <td>{money(p.revenue)}</td>
              <td className={p.profit === null ? "muted" : "positive"}>{money(p.profit)}</td>
              <td>
                <span className={p.margin === null ? "pill missing" : "pill"}>{pct(p.margin)}</span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
function Products({ products, session, dateFrom, dateTo, openProductId }: { products: Product[]; session: Session; dateFrom?: string; dateTo?: string; openProductId?:string }) {
  const {t,locale}=useI18n();const localeCode=locale==="kk"?"kk-KZ":"ru-RU";
  const [search, setSearch] = useState(""),
    [filter, setFilter] = useState("all"),
    [sort, setSort] = useState("name-asc"),
    [statusOverrides, setStatusOverrides] = useState<Record<string,"Active"|"Archived">>({}),
    [selected, setSelected] = useState<Product | null>(null),
    [detail, setDetail] = useState<any>(null),
    [series, setSeries] = useState<ProductPoint[]>([]),
    [seriesLoading, setSeriesLoading] = useState(false),
    [preview, setPreview] = useState<any>(null),
    [message, setMessage] = useState("");
  const headers = { "X-Organization-Id": session.organizationId };
  const normalized=products.map(p=>({...p,productStatus:statusOverrides[p.id]??p.productStatus??(p.status==="archived"?"Archived":"Active")}));
  const filtered = normalized.filter((p) => (p.sku + " " + p.name).toLowerCase().includes(search.toLowerCase()) && (filter === "all" || (filter === "missing" && p.cost === null) || (filter === "profitable" && (p.profit ?? 0) > 0) || (filter === "loss" && (p.profit ?? 0) < 0) || (filter === "archived" && p.productStatus === "Archived"))).sort((a,b)=>{const [key,direction]=sort.split("-");const av=key==="name"?a.name.toLocaleLowerCase("ru"):key==="sku"?a.sku.toLocaleLowerCase("ru"):(a as any)[key]??Number.NEGATIVE_INFINITY;const bv=key==="name"?b.name.toLocaleLowerCase("ru"):key==="sku"?b.sku.toLocaleLowerCase("ru"):(b as any)[key]??Number.NEGATIVE_INFINITY;const result=typeof av==="string"?av.localeCompare(bv,"ru"):av-bv;return direction==="desc"?-result:result;});
  const open = async (p: Product) => {
    setSelected(p);
    setDetail(null);
    setSeries([]);
    setSeriesLoading(true);
    const query = new URLSearchParams();
    if (dateFrom) query.set("dateFrom", dateFrom);
    if (dateTo) query.set("dateTo", dateTo);
    const [detailResponse, seriesResponse] = await Promise.all([fetch(`/api/v1/products/${p.id}`, { headers }), fetch(`/api/v1/products/${p.id}/timeseries?${query}`, { headers })]);
    if (detailResponse.ok) setDetail(await detailResponse.json());
    if (seriesResponse.ok) setSeries(await seriesResponse.json());
    else setMessage(t("products.seriesError"));
    setSeriesLoading(false);
  };
  useEffect(()=>{if(!openProductId)return;const product=normalized.find(p=>p.id===openProductId);if(product)open(product);},[openProductId]);
  const upload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const form = new FormData();
    form.append("file", file);
    setMessage(t("products.checking"));
    const r = await fetch("/api/v1/costs/imports/preview", {
      method: "POST",
      headers,
      body: form,
    });
    const data = await r.json();
    if (r.ok) {
      setPreview(data);
      setMessage("");
    } else setMessage(data.title || t("products.importError"));
  };
  const confirm = async () => {
    const r = await fetch(`/api/v1/costs/imports/${preview.id}/confirm`, {
      method: "POST",
      headers,
    });
    if (r.ok) {
      const data = await r.json();
      setMessage(`${t("products.applied")}: ${data.appliedRows}`);
      setPreview(null);
    } else setMessage(t("products.applyError"));
  };
  const addCost = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!selected) return;
    const f = new FormData(e.currentTarget);
    const r = await fetch(`/api/v1/products/${selected.id}/costs`, {
      method: "POST",
      headers: { ...headers, "Content-Type": "application/json" },
      body: JSON.stringify({
        cost: Number(f.get("cost")),
        effectiveFrom: f.get("date"),
      }),
    });
    if (r.ok) {
      setMessage(t("products.costAdded"));
      open(selected);
    } else setMessage((await r.json().catch(() => null))?.title || t("products.error"));
  };
  const toggleStatus = async () => {
    if(!selected)return;const current=statusOverrides[selected.id]??selected.productStatus??(selected.status==="archived"?"Archived":"Active");const next=current==="Archived"?"Active":"Archived";const response=await fetch(`/api/v1/products/${selected.id}/status`,{method:"PUT",headers:{...headers,"Content-Type":"application/json"},body:JSON.stringify({status:next})});
    if(!response.ok){setMessage((await response.json().catch(()=>null))?.title||t("products.statusError"));return;}setStatusOverrides(value=>({...value,[selected.id]:next}));setSelected({...selected,productStatus:next,status:next==="Archived"?"archived":selected.cost===null?"missing-cost":"profitable"});setDetail((value:any)=>value?{...value,status:next}:value);setMessage(next==="Archived"?t("products.movedArchive"):t("products.restored"));
  };
  return (
    <section className="content">
      <div className="title-row">
        <div>
          <span className="eyebrow">{t("products.eyebrow")}</span>
          <h1>{t("products.title")}</h1>
          <p>{t("products.lead")}</p>
        </div>
        <label className="primary file-button">
          {t("products.import")}
          <input type="file" accept=".csv,.xlsx" onChange={upload} />
        </label>
      </div>
      <div className="product-tools">
        <input placeholder={t("products.search")} value={search} onChange={(e) => setSearch(e.target.value)} />
        <select value={filter} onChange={(e) => setFilter(e.target.value)}>
          <option value="all">{t("products.all")}</option><option value="profitable">{t("products.profitable")}</option><option value="loss">{t("products.loss")}</option><option value="missing">{t("products.missing")}</option><option value="archived">{t("products.archived")}</option>
        </select>
        <select value={sort} onChange={(e) => setSort(e.target.value)} aria-label={t("products.sort")}>
          <option value="name-asc">{t("products.nameAsc")}</option><option value="sku-asc">{t("products.skuAsc")}</option><option value="units-desc">{t("products.unitsDesc")}</option><option value="revenue-desc">{t("products.revenueDesc")}</option><option value="profit-desc">{t("products.profitDesc")}</option><option value="profit-asc">{t("products.profitAsc")}</option><option value="margin-desc">{t("products.marginDesc")}</option><option value="coveragePct-asc">{t("products.coverageAsc")}</option>
        </select>
      </div>
      {message && <p className="integration-message">{message}</p>}
      {preview && <ImportPreview preview={preview} onConfirm={confirm} />}
      <article className="table-card standalone">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t("products.product")}</th><th>{t("products.sales")}</th><th>{t("products.revenue")}</th><th>{t("products.expenses")}</th><th>{t("products.profit")}</th><th>{t("products.margin")}</th>
                <th>Coverage</th>
                <th>{t("products.status")}</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((p) => (
                <tr key={p.id} onClick={() => open(p)}>
                  <td>
                    <span className="product-icon">{p.name[0]}</span>
                    <div>
                      <b>{p.name}</b>
                      <small>{p.sku}</small>
                    </div>
                  </td>
                  <td>{p.units}</td>
                  <td>{money(p.revenue)}</td>
                  <td title={`${t("products.direct")}: ${money(p.directExpenses ?? 0)}; ${t("products.allocated")}: ${money(p.allocatedOrganizationExpenses ?? 0)}`}>{money((p.directExpenses ?? 0) + (p.allocatedOrganizationExpenses ?? 0))}</td>
                  <td>{money(p.profit)}</td>
                  <td>{pct(p.margin)}</td>
                  <td>{pct(p.coveragePct ?? (p.cost === null ? 0 : 100))}</td>
                  <td><span className={p.productStatus === "Archived" ? "pill archived" : "pill"}>{p.productStatus === "Archived" ? t("products.archive") : t("products.active")}</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </article>
      {selected && (
        <div className="modal-shade" onClick={() => setSelected(null)}>
          <article className="product-modal" onClick={(e) => e.stopPropagation()}>
            <button className="modal-close" onClick={() => setSelected(null)}>
              <X />
            </button>
            <span className="eyebrow">{selected.sku}</span>
            <h2>{selected.name}</h2>
            <div className="product-status-row"><span className={(statusOverrides[selected.id]??selected.productStatus)==="Archived"?"pill archived":"pill"}>{(statusOverrides[selected.id]??selected.productStatus)==="Archived"?t("products.archive"):t("products.active")}</span>{session.role!=="Viewer"&&<button type="button" className="secondary" onClick={toggleStatus}>{(statusOverrides[selected.id]??selected.productStatus)==="Archived"?t("product.restore"):t("product.archiveAction")}</button>}</div>
            <div className="product-chart-head">
              <div>
                <h3>{t("product.dynamics")}</h3>
                <p>{dateFrom && dateTo ? `${new Date(dateFrom).toLocaleDateString(localeCode)} — ${new Date(dateTo).toLocaleDateString(localeCode)}` : t("product.allPeriod")}</p>
              </div>
              <div className="legend"><span><i className="revenue" />{t("product.revenue")}</span><span><i className="profit" />{t("product.profit")}</span></div>
            </div>
            {seriesLoading ? <p className="muted">{t("product.loading")}</p> : series.length ? (
              <div className="product-chart" aria-label={t("product.chartLabel")}>
                {series.map((point) => {
                  const scale=Math.max(...series.flatMap(x=>[Math.abs(x.revenue),Math.abs(x.operatingProfit)]),1);
                  return <div className="product-bar" key={point.date} title={`${new Date(point.date).toLocaleDateString(localeCode)}: ${t("product.revenue")} ${money(point.revenue)}, ${t("product.profit")} ${money(point.operatingProfit)}, Coverage ${pct(point.coveragePct)}`}>
                    <div><i className="profit-bar" style={{height:`${Math.max(2,Math.abs(point.operatingProfit)/scale*100)}%`}}/><i className="revenue-bar" style={{height:`${Math.max(2,Math.abs(point.revenue)/scale*100)}%`}}/></div>
                    <small>{new Date(point.date).toLocaleDateString(localeCode,{day:"2-digit",month:"short"})}</small>
                  </div>;
                })}
              </div>
            ) : <p className="product-chart-empty">{t("product.emptySeries")}</p>}
            <form className="cost-form" onSubmit={addCost}>
              <label>
                {t("product.cost")}
                <input name="cost" type="number" min="0.01" step="0.01" required />
              </label>
              <label>
                {t("product.effectiveFrom")}
                <input name="date" type="date" required defaultValue={new Date().toISOString().slice(0, 10)} />
              </label>
              <button className="primary">{t("product.add")}</button>
            </form>
            <h3>{t("product.costHistory")}</h3>
            <div className="cost-history">
              {detail?.costHistory?.map((x: any) => (
                <div key={x.id}>
                  <b>{money(x.costAmount)}</b>
                  <span>
                    {t("product.from")} {new Date(x.effectiveFrom).toLocaleDateString(localeCode)} · {x.source}
                  </span>
                </div>
              ))}
              {detail && !detail.costHistory?.length && <p>{t("product.historyEmpty")}</p>}
            </div>
          </article>
        </div>
      )}
    </section>
  );
}
function ImportPreview({ preview, onConfirm }: { preview: any; onConfirm: () => void }) {
  const { t } = useI18n();
  return (
    <article className="import-preview">
      <div>
        <h2>{t("import.preview")}</h2>
        <p>
          {t("import.rows")}: {preview.totalRows} · {t("import.matched")}: {preview.matchedRows} · {t("import.unmatched")}: {preview.unmatchedRows} · {t("import.errors")}: {preview.errorRows} · {t("import.duplicates")}: {preview.duplicateRows}
        </p>
      </div>
      <button className="primary" onClick={onConfirm} disabled={!preview.expectedChanges}>
        {t("import.apply")} {preview.expectedChanges} {t("import.changes")}
      </button>
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>{t("import.row")}</th>
              <th>SKU</th>
              <th>{t("import.cost")}</th>
              <th>{t("import.date")}</th>
              <th>{t("import.status")}</th>
            </tr>
          </thead>
          <tbody>
            {preview.rows.map((r: any) => (
              <tr key={r.rowNumber}>
                <td>{r.rowNumber}</td>
                <td>{r.sku}</td>
                <td>{money(r.costAmount)}</td>
                <td>{r.effectiveFrom || "—"}</td>
                <td>
                  <span className={r.status === "Valid" ? "pill" : "pill missing"}>{r.error || r.status}</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </article>
  );
}
function EmptyPage({ title, text, icon }: { title: string; text: string; icon: React.ReactNode }) {
  return (
    <section className="content">
      <div className="empty">
        <span>{icon}</span>
        <h1>{title}</h1>
        <p>{text}</p>
        <button className="primary">Перейти к интеграциям</button>
      </div>
    </section>
  );
}

createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <I18nProvider><App /></I18nProvider>
  </React.StrictMode>,
);
