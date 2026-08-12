import React, { createContext, useContext, useEffect, useMemo, useState } from "react";

const ordersRu={
  "orders.eyebrow":"ПРОДАЖИ","orders.title":"Заказы","orders.lead":"Фильтры и подробная декомпозиция финансового результата.","orders.search":"Поиск заказа","orders.number":"Номер заказа","orders.status":"Статус","orders.allStatuses":"Все статусы","orders.pending":"В обработке","orders.dateFrom":"Заказы с даты","orders.dateTo":"Заказы по дату","orders.from":"С","orders.to":"По","orders.product":"Товар","orders.allProducts":"Все товары","orders.profitFrom":"Прибыль от","orders.profitTo":"Прибыль до","orders.loading":"Загрузка…","orders.apply":"Применить","orders.order":"Заказ","orders.date":"Дата","orders.amount":"Сумма","orders.fee":"Комиссия","orders.delivery":"Доставка","orders.profit":"Прибыль","orders.calculation":"Расчёт","orders.complete":"Полный","orders.needsCost":"Нужна себестоимость","orders.empty":"По заданным фильтрам заказов нет.","orders.found":"Найдено","orders.code":"Код","orders.payment":"Оплата","orders.unspecified":"не указана","orders.completed":"Завершён","orders.dateMissing":"дата отсутствует","orders.fallback":"Дата завершения отсутствует — расчёт выполнен по дате создания.","orders.returnRequested":"Запрошен возврат доставки — заказ ещё не считается окончательно возвращённым.","orders.revenue":"Выручка","orders.fees":"Комиссии","orders.variableCosts":"Прочие переменные расходы","orders.operatingProfit":"Операционная прибыль","orders.margin":"Маржа","orders.statusHistory":"История статусов","orders.historyEmpty":"История появится после следующей синхронизации.","orders.lines":"Товарные строки","orders.quantity":"Количество","orders.unitCost":"Себестоимость единицы"
  ,"orders.skuUnmapped":"SKU не сопоставлен","orders.pieces":"шт.","orders.lineRevenue":"выручка","orders.lineCost":"себестоимость","orders.lineFee":"комиссия","orders.lineDelivery":"доставка","orders.lineOther":"прочие"
} as const;
const ordersKk:Record<keyof typeof ordersRu,string>={
  "orders.eyebrow":"САТЫЛЫМДАР","orders.title":"Тапсырыстар","orders.lead":"Қаржылық нәтижені сүзу және толық жіктеу.","orders.search":"Тапсырысты іздеу","orders.number":"Тапсырыс нөмірі","orders.status":"Күйі","orders.allStatuses":"Барлық күйлер","orders.pending":"Өңделуде","orders.dateFrom":"Бастапқы күн","orders.dateTo":"Соңғы күн","orders.from":"Бастап","orders.to":"Дейін","orders.product":"Тауар","orders.allProducts":"Барлық тауарлар","orders.profitFrom":"Пайда бастап","orders.profitTo":"Пайда дейін","orders.loading":"Жүктелуде…","orders.apply":"Қолдану","orders.order":"Тапсырыс","orders.date":"Күні","orders.amount":"Сома","orders.fee":"Комиссия","orders.delivery":"Жеткізу","orders.profit":"Пайда","orders.calculation":"Есеп","orders.complete":"Толық","orders.needsCost":"Өзіндік құн қажет","orders.empty":"Берілген сүзгілер бойынша тапсырыс жоқ.","orders.found":"Табылды","orders.code":"Код","orders.payment":"Төлем","orders.unspecified":"көрсетілмеген","orders.completed":"Аяқталды","orders.dateMissing":"күні жоқ","orders.fallback":"Аяқталу күні жоқ — есеп құру күні бойынша орындалды.","orders.returnRequested":"Жеткізуді қайтару сұралды — тапсырыс әлі толық қайтарылған жоқ.","orders.revenue":"Түсім","orders.fees":"Комиссиялар","orders.variableCosts":"Басқа айнымалы шығындар","orders.operatingProfit":"Операциялық пайда","orders.margin":"Маржа","orders.statusHistory":"Күйлер тарихы","orders.historyEmpty":"Тарих келесі синхрондаудан кейін пайда болады.","orders.lines":"Тауар жолдары","orders.quantity":"Саны","orders.unitCost":"Бірлік өзіндік құны"
  ,"orders.skuUnmapped":"SKU сәйкестендірілмеген","orders.pieces":"дана","orders.lineRevenue":"түсім","orders.lineCost":"өзіндік құн","orders.lineFee":"комиссия","orders.lineDelivery":"жеткізу","orders.lineOther":"басқа"
};
const productsRu={
  "products.eyebrow":"КАТАЛОГ","products.title":"Товары","products.lead":"Продажи, история себестоимости и Coverage по SKU.","products.import":"Импорт CSV/XLSX","products.search":"Поиск по SKU или названию","products.all":"Все товары","products.profitable":"Прибыльные","products.loss":"Убыточные","products.missing":"Без себестоимости","products.archived":"Архивные","products.sort":"Сортировка товаров","products.nameAsc":"Название: А–Я","products.skuAsc":"SKU: А–Я","products.unitsDesc":"Продажи: больше","products.revenueDesc":"Выручка: больше","products.profitDesc":"Прибыль: больше","products.profitAsc":"Прибыль: меньше","products.marginDesc":"Маржа: больше","products.coverageAsc":"Coverage: меньше","products.product":"Товар","products.sales":"Продажи","products.revenue":"Выручка","products.expenses":"Расходы","products.profit":"Прибыль","products.margin":"Маржа","products.status":"Статус","products.archive":"Архив","products.active":"Активен","products.direct":"Прямые","products.allocated":"распределённые","products.checking":"Проверяем файл…","products.importError":"Ошибка импорта","products.applied":"Применено строк","products.applyError":"Не удалось применить импорт","products.costAdded":"Себестоимость добавлена","products.error":"Ошибка","products.statusError":"Не удалось изменить статус товара","products.movedArchive":"Товар перемещён в архив","products.restored":"Товар возвращён в активные","products.seriesError":"Не удалось загрузить динамику товара"
} as const;
const productsKk:Record<keyof typeof productsRu,string>={
  "products.eyebrow":"КАТАЛОГ","products.title":"Тауарлар","products.lead":"SKU бойынша сатылым, өзіндік құн тарихы және Coverage.","products.import":"CSV/XLSX импорты","products.search":"SKU немесе атауы бойынша іздеу","products.all":"Барлық тауарлар","products.profitable":"Пайдалы","products.loss":"Залалды","products.missing":"Өзіндік құнсыз","products.archived":"Мұрағатта","products.sort":"Тауарларды сұрыптау","products.nameAsc":"Атауы: А–Я","products.skuAsc":"SKU: А–Я","products.unitsDesc":"Сатылым: көп","products.revenueDesc":"Түсім: көп","products.profitDesc":"Пайда: көп","products.profitAsc":"Пайда: аз","products.marginDesc":"Маржа: көп","products.coverageAsc":"Coverage: аз","products.product":"Тауар","products.sales":"Сатылым","products.revenue":"Түсім","products.expenses":"Шығындар","products.profit":"Пайда","products.margin":"Маржа","products.status":"Күйі","products.archive":"Мұрағат","products.active":"Белсенді","products.direct":"Тікелей","products.allocated":"бөлінген","products.checking":"Файл тексерілуде…","products.importError":"Импорт қатесі","products.applied":"Қолданылған жолдар","products.applyError":"Импортты қолдану мүмкін болмады","products.costAdded":"Өзіндік құн қосылды","products.error":"Қате","products.statusError":"Тауар күйін өзгерту мүмкін болмады","products.movedArchive":"Тауар мұрағатқа көшірілді","products.restored":"Тауар белсендіге қайтарылды","products.seriesError":"Тауар динамикасын жүктеу мүмкін болмады"
};

export const ru = {
  ...ordersRu,
  ...productsRu,
  "brand.name": "Seller Finance",
  "common.wait": "Подождите…",
  "common.logout": "выйти",
  "common.create": "Создать",
  "auth.login.title": "Войти",
  "auth.login.lead": "Управляйте прибылью и себестоимостью в одном месте.",
  "auth.login.submit": "Войти",
  "auth.register.title": "Создать аккаунт",
  "auth.register.lead": "Создайте защищённое рабочее пространство продавца.",
  "auth.register.submit": "Зарегистрироваться",
  "auth.register.name": "Ваше имя",
  "auth.register.organization": "Название организации",
  "auth.email": "Email",
  "auth.password": "Пароль",
  "auth.forgot.title": "Восстановить пароль",
  "auth.forgot.lead": "Отправим одноразовую ссылку, если аккаунт существует.",
  "auth.forgot.submit": "Отправить инструкцию",
  "auth.forgot.link": "Забыли пароль?",
  "auth.forgot.success": "Если аккаунт существует, инструкция отправлена на email.",
  "auth.confirm.success": "Проверьте почту и подтвердите email.",
  "auth.switch.login": "Уже есть аккаунт? Войти",
  "auth.switch.register": "Нет аккаунта? Зарегистрироваться",
  "auth.switch.back": "Вернуться ко входу",
  "auth.error": "Ошибка авторизации",
  "auth.session.error": "Сессия не создана",
  "auth.hero.eyebrow": "Финансы маркетплейса без слепых зон",
  "auth.hero.title": "Знайте реальную прибыль каждого заказа и товара.",
  "auth.hero.lead": "Себестоимость, комиссии, доставка и расходы — в едином расчёте.",
  "reset.title": "Новый пароль",
  "reset.submit": "Сохранить пароль",
  "reset.success": "Пароль изменён. Теперь можно войти.",
  "reset.error": "Ссылка недействительна или пароль не соответствует требованиям.",
  "nav.dashboard": "Обзор",
  "nav.products": "Товары",
  "nav.orders": "Заказы",
  "nav.abc": "ABC-анализ",
  "nav.exports": "Экспорт",
  "nav.integrations": "Интеграции",
  "nav.expenses": "Расходы",
  "nav.fees": "Комиссии",
  "nav.settings": "Настройки",
  "nav.admin": "SaaS Admin",
  "nav.analytics": "АНАЛИТИКА",
  "nav.management": "УПРАВЛЕНИЕ",
  "plan.trial": "Тариф Trial",
  "plan.pilot": "Пилотный период",
  "plan.organization": "1 организация",
  "header.menu": "Открыть меню",
  "header.organization": "Организация",
  "header.createOrganization": "Создать организацию",
  "header.organizationPrompt": "Название новой организации",
  "header.organizationCreateError": "Не удалось создать организацию",
  "language.ru": "Русский",
  "language.kk": "Қазақша",
  "dashboard.title":"Финансовый обзор","dashboard.lead":"Главное о продажах и прибыли за выбранный период.","dashboard.sync":"Синхронизировать","dashboard.today":"Сегодня","dashboard.yesterday":"Вчера","dashboard.days7":"Последние 7 дней","dashboard.days30":"Последние 30 дней","dashboard.days90":"Последние 90 дней","dashboard.currentMonth":"Текущий месяц","dashboard.previousMonth":"Предыдущий месяц","dashboard.custom":"Произвольный период","dashboard.periodFrom":"Начало периода","dashboard.periodTo":"Конец периода","dashboard.completeCosts":"Только продажи с полной себестоимостью","dashboard.loaded":"Данные загружены из PostgreSQL","dashboard.preliminary":"Прибыль предварительная","dashboard.fillCosts":"Заполнить себестоимость","dashboard.revenue":"Выручка","dashboard.units":"Продано единиц","dashboard.orders":"заказов","dashboard.fullCost":"Полная себестоимость","dashboard.incompleteCoverage":"Неполное покрытие","dashboard.grossProfit":"Валовая прибыль","dashboard.fees":"Комиссии","dashboard.delivery":"Доставка","dashboard.periodExpenses":"Расходы периода","dashboard.expenseKinds":"Прямые и общеорганизационные","dashboard.operatingProfit":"Операционная прибыль","dashboard.operatingMargin":"Операционная маржа","dashboard.noRevenue":"Нет выручки","dashboard.prelimShort":"Предварительно","dashboard.chartTitle":"Выручка и прибыль","dashboard.chartLead":"Динамика по дням","dashboard.profit":"Прибыль","dashboard.dataCompleteness":"Полнота данных","dashboard.costCoverage":"Покрытие себестоимостью","dashboard.revenueWord":"выручки","dashboard.checkProducts":"Проверить товары","dashboard.topProfit":"TOP-10 по прибыли","dashboard.topLoss":"TOP-10 по убытку","dashboard.periodSelected":"За выбранный период","dashboard.loss":"Убыток","dashboard.all":"Все →","dashboard.attention":"Требует внимания","dashboard.accuracyBlockers":"Что мешает точному расчёту","dashboard.noCost":"Нет себестоимости","dashboard.add":"Добавить","dashboard.negativeMargin":"Отрицательная маржа","dashboard.open":"Открыть","dashboard.syncError":"Ошибка синхронизации","dashboard.checkRequired":"Требуется проверка","dashboard.check":"Проверить","dashboard.noProblems":"Проблем за выбранный период нет.",
  "dashboard.missingCostPrefix":"Для","dashboard.missingCostSuffix":"выручки не указана себестоимость.","dashboard.problemCountSuffix":"проблем требует внимания",
} as const;

export type TranslationKey=keyof typeof ru;
const kk:Record<TranslationKey,string>={
  ...ordersKk,
  ...productsKk,
  "brand.name":"Seller Finance","common.wait":"Күте тұрыңыз…","common.logout":"шығу","common.create":"Құру",
  "auth.login.title":"Кіру","auth.login.lead":"Пайда мен өзіндік құнды бір жерден басқарыңыз.","auth.login.submit":"Кіру",
  "auth.register.title":"Тіркелгі ашу","auth.register.lead":"Сатушының қорғалған жұмыс кеңістігін құрыңыз.","auth.register.submit":"Тіркелу","auth.register.name":"Атыңыз","auth.register.organization":"Ұйым атауы",
  "auth.email":"Email","auth.password":"Құпиясөз","auth.forgot.title":"Құпиясөзді қалпына келтіру","auth.forgot.lead":"Тіркелгі бар болса, бір реттік сілтеме жібереміз.","auth.forgot.submit":"Нұсқаулықты жіберу","auth.forgot.link":"Құпиясөзді ұмыттыңыз ба?","auth.forgot.success":"Тіркелгі бар болса, нұсқаулық email-ға жіберілді.","auth.confirm.success":"Поштаңызды тексеріп, email-ды растаңыз.",
  "auth.switch.login":"Тіркелгіңіз бар ма? Кіру","auth.switch.register":"Тіркелгіңіз жоқ па? Тіркелу","auth.switch.back":"Кіруге оралу","auth.error":"Авторизация қатесі","auth.session.error":"Сессия құрылмады",
  "auth.hero.eyebrow":"Маркетплейс қаржысы айқын көрінеді","auth.hero.title":"Әр тапсырыс пен тауардың нақты пайдасын біліңіз.","auth.hero.lead":"Өзіндік құн, комиссия, жеткізу және шығындар — бір есепте.",
  "reset.title":"Жаңа құпиясөз","reset.submit":"Құпиясөзді сақтау","reset.success":"Құпиясөз өзгертілді. Енді кіруге болады.","reset.error":"Сілтеме жарамсыз немесе құпиясөз талаптарға сай емес.",
  "nav.dashboard":"Шолу","nav.products":"Тауарлар","nav.orders":"Тапсырыстар","nav.abc":"ABC-талдау","nav.exports":"Экспорт","nav.integrations":"Интеграциялар","nav.expenses":"Шығындар","nav.fees":"Комиссиялар","nav.settings":"Баптаулар","nav.admin":"SaaS Admin","nav.analytics":"ТАЛДАУ","nav.management":"БАСҚАРУ","plan.trial":"Trial тарифі","plan.pilot":"Сынақ кезеңі","plan.organization":"1 ұйым",
  "header.menu":"Мәзірді ашу","header.organization":"Ұйым","header.createOrganization":"Ұйым құру","header.organizationPrompt":"Жаңа ұйымның атауы","header.organizationCreateError":"Ұйымды құру мүмкін болмады","language.ru":"Русский","language.kk":"Қазақша",
  "dashboard.title":"Қаржылық шолу","dashboard.lead":"Таңдалған кезеңдегі сатылым мен пайда туралы негізгі мәлімет.","dashboard.sync":"Синхрондау","dashboard.today":"Бүгін","dashboard.yesterday":"Кеше","dashboard.days7":"Соңғы 7 күн","dashboard.days30":"Соңғы 30 күн","dashboard.days90":"Соңғы 90 күн","dashboard.currentMonth":"Ағымдағы ай","dashboard.previousMonth":"Алдыңғы ай","dashboard.custom":"Еркін кезең","dashboard.periodFrom":"Кезеңнің басы","dashboard.periodTo":"Кезеңнің соңы","dashboard.completeCosts":"Тек толық өзіндік құны бар сатылымдар","dashboard.loaded":"Деректер PostgreSQL-ден жүктелді","dashboard.preliminary":"Пайда алдын ала есептелген","dashboard.fillCosts":"Өзіндік құнды толтыру","dashboard.revenue":"Түсім","dashboard.units":"Сатылған дана","dashboard.orders":"тапсырыс","dashboard.fullCost":"Толық өзіндік құн","dashboard.incompleteCoverage":"Толық емес қамту","dashboard.grossProfit":"Жалпы пайда","dashboard.fees":"Комиссиялар","dashboard.delivery":"Жеткізу","dashboard.periodExpenses":"Кезең шығындары","dashboard.expenseKinds":"Тікелей және жалпы ұйымдық","dashboard.operatingProfit":"Операциялық пайда","dashboard.operatingMargin":"Операциялық маржа","dashboard.noRevenue":"Түсім жоқ","dashboard.prelimShort":"Алдын ала","dashboard.chartTitle":"Түсім және пайда","dashboard.chartLead":"Күндер бойынша динамика","dashboard.profit":"Пайда","dashboard.dataCompleteness":"Деректердің толықтығы","dashboard.costCoverage":"Өзіндік құнмен қамту","dashboard.revenueWord":"түсім","dashboard.checkProducts":"Тауарларды тексеру","dashboard.topProfit":"Пайда бойынша TOP-10","dashboard.topLoss":"Залал бойынша TOP-10","dashboard.periodSelected":"Таңдалған кезеңде","dashboard.loss":"Залал","dashboard.all":"Барлығы →","dashboard.attention":"Назар аудару қажет","dashboard.accuracyBlockers":"Дәл есептеуге кедергілер","dashboard.noCost":"Өзіндік құн жоқ","dashboard.add":"Қосу","dashboard.negativeMargin":"Теріс маржа","dashboard.open":"Ашу","dashboard.syncError":"Синхрондау қатесі","dashboard.checkRequired":"Тексеру қажет","dashboard.check":"Тексеру","dashboard.noProblems":"Таңдалған кезеңде мәселе жоқ."
  ,"dashboard.missingCostPrefix":"Өзіндік құны көрсетілмеген","dashboard.missingCostSuffix":"түсім.","dashboard.problemCountSuffix":"мәселе назар аударуды қажет етеді"
};

export type Locale="ru"|"kk";
const catalogs:Record<Locale,Record<TranslationKey,string>>={ru,kk};
type I18nValue={locale:Locale;setLocale:(locale:Locale)=>void;t:(key:TranslationKey)=>string};
const I18nContext=createContext<I18nValue|null>(null);

export function I18nProvider({children}:{children:React.ReactNode}){
  const [locale,setLocaleState]=useState<Locale>(()=>localStorage.getItem("seller-finance-locale")==="kk"?"kk":"ru");
  useEffect(()=>{document.documentElement.lang=locale;},[locale]);
  const value=useMemo<I18nValue>(()=>({locale,setLocale(next){localStorage.setItem("seller-finance-locale",next);document.documentElement.lang=next;setLocaleState(next);},t:key=>catalogs[locale][key]}),[locale]);
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}
export function useI18n(){const value=useContext(I18nContext);if(!value)throw new Error("I18nProvider is required");return value;}
