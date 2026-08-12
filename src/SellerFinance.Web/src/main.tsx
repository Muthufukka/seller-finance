import React, { useEffect, useMemo, useState } from 'react'
import { createRoot } from 'react-dom/client'
import { BarChart3, Box, ChevronDown, CircleAlert, Download, LayoutDashboard, LogIn, Menu, PackageSearch, RefreshCw, Search, Settings, ShoppingBag, Sparkles, TrendingDown, TrendingUp, WalletCards, X } from 'lucide-react'
import './styles.css'

type Summary = { revenue:number; orders:number; units:number; cogs:number|null; grossProfit:number; marketplaceFees:number; delivery:number; operatingProfit:number; operatingMarginPct:number|null; coveragePct:number; isPreliminary:boolean }
type Product = { id:string; sku:string; name:string; units:number; revenue:number; cogs:number|null; profit:number|null; margin:number|null; cost:number|null; status:string }
type Point = { date:string; revenue:number; profit:number }
type Page = 'dashboard'|'products'|'orders'|'abc'
type Session = { userId:string; email:string; displayName:string; organizationId:string; organizationName:string; role:string; plan:string }

const fallback:{summary:Summary; products:Product[]; timeseries:Point[]} = {
  summary:{revenue:173835,orders:6,units:12,cogs:null,grossProfit:83835,marketplaceFees:17798,delivery:3850,operatingProfit:49687,operatingMarginPct:28.58,coveragePct:92.53,isPreliminary:true},
  products:[
    {id:'p1',sku:'HOME-101',name:'Органайзер для кухни',units:5,revenue:62475,cogs:36000,profit:18865,margin:30.2,cost:7200,status:'profitable'},
    {id:'p2',sku:'BEAUTY-220',name:'Набор косметичек',units:3,revenue:55470,cogs:26700,profit:22173,margin:40,cost:8900,status:'profitable'},
    {id:'p3',sku:'TECH-044',name:'Настольная LED-лампа',units:3,revenue:42900,cogs:27300,profit:10400,margin:24.2,cost:9100,status:'profitable'},
    {id:'p4',sku:'KIDS-018',name:'Развивающий набор',units:1,revenue:12990,cogs:null,profit:null,margin:null,cost:null,status:'missing-cost'}],
  timeseries:[{date:'2026-08-06',revenue:24990,profit:7166},{date:'2026-08-07',revenue:18490,profit:7124},{date:'2026-08-08',revenue:42900,profit:10400},{date:'2026-08-09',revenue:12990,profit:1051},{date:'2026-08-10',revenue:37485,profit:10999},{date:'2026-08-11',revenue:36980,profit:13850}]
}

const money=(v:number|null)=>v===null?'—':new Intl.NumberFormat('ru-RU',{maximumFractionDigits:0}).format(v)+' ₸'
const pct=(v:number|null)=>v===null?'—':v.toFixed(1).replace('.',',')+'%'

function App(){
  const [page,setPage]=useState<Page>('dashboard'), [menu,setMenu]=useState(false), [loading,setLoading]=useState(true)
  const [session,setSession]=useState<Session|null>(null), [authReady,setAuthReady]=useState(false)
  const [summary,setSummary]=useState(fallback.summary), [products,setProducts]=useState(fallback.products), [points,setPoints]=useState(fallback.timeseries)
  useEffect(()=>{ fetch('/api/v1/session').then(async r=>r.ok?setSession(await r.json()):setSession(null)).finally(()=>setAuthReady(true)) },[])
  useEffect(()=>{ if(!session)return; const headers={'X-Organization-Id':session.organizationId}; Promise.all([
    fetch('/api/v1/analytics/summary',{headers}).then(r=>r.ok?r.json():Promise.reject()),
    fetch('/api/v1/analytics/products',{headers}).then(r=>r.ok?r.json():Promise.reject()),
    fetch('/api/v1/analytics/timeseries',{headers}).then(r=>r.ok?r.json():Promise.reject())
  ]).then(([s,p,t])=>{setSummary(s);setProducts(p);setPoints(t)}).catch(()=>{}).finally(()=>setLoading(false)) },[session])
  if(!authReady)return <div className="auth-loading">Seller Finance</div>
  if(!session)return <AuthScreen onAuthenticated={setSession}/>
  const navigate=(p:Page)=>{setPage(p);setMenu(false)}
  return <div className="app">
    <Sidebar page={page} open={menu} onClose={()=>setMenu(false)} onNav={navigate}/>
    <main>
      <header><button className="icon mobile" onClick={()=>setMenu(true)} aria-label="Открыть меню"><Menu/></button><div className="org"><span className="orgmark">{session.organizationName[0]}</span><div><b>{session.organizationName}</b><small>{session.role}</small></div><ChevronDown size={16}/></div><div className="head-actions"><button className="icon"><Search/></button><button className="avatar" title={`${session.email} — выйти`} onClick={async()=>{await fetch('/api/v1/auth/logout',{method:'POST'});setSession(null)}}>{(session.displayName||session.email).slice(0,2).toUpperCase()}</button></div></header>
      {page==='dashboard'&&<Dashboard summary={summary} products={products} points={points} loading={loading}/>} 
      {page==='products'&&<Products products={products}/>} 
      {page==='orders'&&<EmptyPage title="Заказы" text="Здесь появятся заказы после синхронизации Kaspi." icon={<ShoppingBag/>}/>} 
      {page==='abc'&&<EmptyPage title="ABC-анализ" text="Товары будут распределены по вкладу в операционную прибыль." icon={<BarChart3/>}/>} 
    </main>
  </div>
}

function AuthScreen({onAuthenticated}:{onAuthenticated:(session:Session)=>void}){
 const [register,setRegister]=useState(false), [busy,setBusy]=useState(false), [error,setError]=useState('')
 const submit=async(e:React.FormEvent<HTMLFormElement>)=>{e.preventDefault();setBusy(true);setError('');const form=new FormData(e.currentTarget);const body=register?{email:form.get('email'),password:form.get('password'),displayName:form.get('displayName'),organizationName:form.get('organizationName')}:{email:form.get('email'),password:form.get('password')};try{const response=await fetch(`/api/v1/auth/${register?'register':'login'}`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});if(!response.ok){const data=await response.json().catch(()=>null);throw new Error(data?.message||data?.title||'Не удалось войти')}const sessionResponse=await fetch('/api/v1/session');if(!sessionResponse.ok)throw new Error('Сессия не создана');onAuthenticated(await sessionResponse.json())}catch(ex){setError(ex instanceof Error?ex.message:'Ошибка авторизации')}finally{setBusy(false)}}
 return <div className="auth-page"><section className="auth-panel"><div className="auth-brand"><span><WalletCards/></span><div>Seller<b>Finance</b></div></div><h1>{register?'Создать аккаунт':'Войти'}</h1><p>{register?'Создайте защищённое рабочее пространство продавца.':'Управляйте прибылью и себестоимостью в одном месте.'}</p><form onSubmit={submit}>{register&&<><label>Ваше имя<input name="displayName" required autoComplete="name"/></label><label>Название организации<input name="organizationName" required/></label></>}<label>Email<input name="email" type="email" required autoComplete="email"/></label><label>Пароль<input name="password" type="password" minLength={8} required autoComplete={register?'new-password':'current-password'}/></label>{error&&<div className="auth-error">{error}</div>}<button className="primary" disabled={busy}><LogIn/>{busy?'Подождите…':register?'Зарегистрироваться':'Войти'}</button></form><button className="auth-switch" onClick={()=>{setRegister(!register);setError('')}}>{register?'Уже есть аккаунт? Войти':'Нет аккаунта? Зарегистрироваться'}</button></section><aside className="auth-hero"><span>Финансы маркетплейса без слепых зон</span><h2>Знайте реальную прибыль каждого заказа и товара.</h2><p>Себестоимость, комиссии, доставка и расходы — в едином расчёте.</p></aside></div>
}

function Sidebar({page,open,onClose,onNav}:{page:Page;open:boolean;onClose:()=>void;onNav:(p:Page)=>void}){
 const nav:[Page,string,React.ReactNode][]=[['dashboard','Обзор',<LayoutDashboard/>],['products','Товары',<Box/>],['orders','Заказы',<ShoppingBag/>],['abc','ABC-анализ',<BarChart3/>]]
 return <><aside className={open?'open':''}><div className="brand"><span><WalletCards/></span><div>Seller<b>Finance</b></div><button className="icon mobile" onClick={onClose}><X/></button></div><nav><small>АНАЛИТИКА</small>{nav.map(([id,label,icon])=><button key={id} className={page===id?'active':''} onClick={()=>onNav(id)}>{icon}{label}</button>)}<small>УПРАВЛЕНИЕ</small><button><RefreshCw/>Интеграции<span className="dot"/></button><button><Settings/>Настройки</button></nav><div className="plan"><div><Sparkles size={16}/>Тариф Pro</div><b>8 дней до оплаты</b><span><i/></span><small>2 из 3 магазинов</small></div></aside>{open&&<div className="shade" onClick={onClose}/>}</>
}

function Dashboard({summary,products,points,loading}:{summary:Summary;products:Product[];points:Point[];loading:boolean}){
 const max=Math.max(...points.map(x=>x.revenue)); const problems=products.filter(x=>x.cost===null)
 return <section className="content">
  <div className="title-row"><div><span className="eyebrow">12 августа 2026</span><h1>Финансовый обзор</h1><p>Главное о продажах и прибыли за выбранный период.</p></div><div className="actions"><button className="secondary"><Download/>Экспорт</button><button className="primary"><RefreshCw className={loading?'spin':''}/>Синхронизировать</button></div></div>
  <div className="toolbar"><button>Последние 7 дней <ChevronDown/></button><span>06 авг — 12 авг</span><span className="sync"><i/> Данные обновлены 4 мин назад</span></div>
  {summary.isPreliminary&&<div className="notice"><CircleAlert/><div><b>Прибыль предварительная</b><span>Для {money(summary.revenue*(100-summary.coveragePct)/100)} выручки не указана себестоимость.</span></div><button>Заполнить себестоимость</button></div>}
  <div className="kpis"><Kpi label="Выручка" value={money(summary.revenue)} delta="+12,4%" good/><Kpi label="Операционная прибыль" value={money(summary.operatingProfit)} delta="+8,7%" good/><Kpi label="Маржинальность" value={pct(summary.operatingMarginPct)} delta="−1,2 п.п."/><Kpi label="Заказы" value={String(summary.orders)} sub={`${summary.units} единиц`}/></div>
  <div className="grid"><article className="chart-card"><div className="card-head"><div><h2>Выручка и прибыль</h2><p>Динамика по дням</p></div><div className="legend"><span><i className="revenue"/>Выручка</span><span><i className="profit"/>Прибыль</span></div></div><div className="chart">{points.map(p=><div className="bar-col" key={p.date}><div className="bars"><i className="profit-bar" style={{height:`${p.profit/max*100}%`}}/><i className="revenue-bar" style={{height:`${p.revenue/max*100}%`}}/></div><small>{new Date(p.date).toLocaleDateString('ru-RU',{day:'2-digit',month:'short'})}</small></div>)}</div></article>
   <article className="coverage"><div className="card-head"><div><h2>Полнота данных</h2><p>Покрытие себестоимостью</p></div></div><div className="ring" style={{'--pct':`${summary.coveragePct*3.6}deg`} as React.CSSProperties}><div><b>{pct(summary.coveragePct)}</b><span>выручки</span></div></div><p><i/> {problems.length} товар требует внимания</p><button>Проверить товары <span>→</span></button></article>
  </div>
  <div className="bottom-grid"><article className="table-card"><div className="card-head"><div><h2>Товары по прибыли</h2><p>За выбранный период</p></div><button>Все товары →</button></div><ProductTable products={products.slice(0,4)}/></article><article className="problems"><div className="card-head"><div><h2>Требует внимания</h2><p>Что мешает точному расчёту</p></div><span>{problems.length}</span></div>{problems.map(p=><div className="problem" key={p.id}><PackageSearch/><div><b>Нет себестоимости</b><span>{p.name} · {p.sku}</span></div><button>Добавить</button></div>)}<div className="problem warning"><TrendingDown/><div><b>Маржа снизилась</b><span>Набор косметичек · −4,1 п.п.</span></div><button>Открыть</button></div></article></div>
 </section>
}

function Kpi({label,value,delta,sub,good}:{label:string;value:string;delta?:string;sub?:string;good?:boolean}){return <article className="kpi"><span>{label}</span><b>{value}</b><div className={good?'good':''}>{good?<TrendingUp/>:delta?<TrendingDown/>:null}{delta&&<strong>{delta}</strong>}{sub&&<small>{sub}</small>}{delta&&<small>к прошлой неделе</small>}</div></article>}
function ProductTable({products}:{products:Product[]}){return <div className="table-wrap"><table><thead><tr><th>Товар</th><th>Выручка</th><th>Прибыль</th><th>Маржа</th></tr></thead><tbody>{products.map(p=><tr key={p.id}><td><span className="product-icon">{p.name[0]}</span><div><b>{p.name}</b><small>{p.sku}</small></div></td><td>{money(p.revenue)}</td><td className={p.profit===null?'muted':'positive'}>{money(p.profit)}</td><td><span className={p.margin===null?'pill missing':'pill'}>{pct(p.margin)}</span></td></tr>)}</tbody></table></div>}
function Products({products}:{products:Product[]}){return <section className="content"><div className="title-row"><div><span className="eyebrow">КАТАЛОГ</span><h1>Товары</h1><p>Продажи, себестоимость и прибыль по SKU.</p></div><button className="primary">Импорт себестоимости</button></div><article className="table-card standalone"><ProductTable products={products}/></article></section>}
function EmptyPage({title,text,icon}:{title:string;text:string;icon:React.ReactNode}){return <section className="content"><div className="empty"><span>{icon}</span><h1>{title}</h1><p>{text}</p><button className="primary">Перейти к интеграциям</button></div></section>}

createRoot(document.getElementById('root')!).render(<React.StrictMode><App/></React.StrictMode>)
