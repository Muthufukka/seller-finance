import React, { createContext, useContext, useEffect, useMemo, useState } from "react";

export const ru = {
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
} as const;

export type TranslationKey=keyof typeof ru;
const kk:Record<TranslationKey,string>={
  "brand.name":"Seller Finance","common.wait":"Күте тұрыңыз…","common.logout":"шығу","common.create":"Құру",
  "auth.login.title":"Кіру","auth.login.lead":"Пайда мен өзіндік құнды бір жерден басқарыңыз.","auth.login.submit":"Кіру",
  "auth.register.title":"Тіркелгі ашу","auth.register.lead":"Сатушының қорғалған жұмыс кеңістігін құрыңыз.","auth.register.submit":"Тіркелу","auth.register.name":"Атыңыз","auth.register.organization":"Ұйым атауы",
  "auth.email":"Email","auth.password":"Құпиясөз","auth.forgot.title":"Құпиясөзді қалпына келтіру","auth.forgot.lead":"Тіркелгі бар болса, бір реттік сілтеме жібереміз.","auth.forgot.submit":"Нұсқаулықты жіберу","auth.forgot.link":"Құпиясөзді ұмыттыңыз ба?","auth.forgot.success":"Тіркелгі бар болса, нұсқаулық email-ға жіберілді.","auth.confirm.success":"Поштаңызды тексеріп, email-ды растаңыз.",
  "auth.switch.login":"Тіркелгіңіз бар ма? Кіру","auth.switch.register":"Тіркелгіңіз жоқ па? Тіркелу","auth.switch.back":"Кіруге оралу","auth.error":"Авторизация қатесі","auth.session.error":"Сессия құрылмады",
  "auth.hero.eyebrow":"Маркетплейс қаржысы айқын көрінеді","auth.hero.title":"Әр тапсырыс пен тауардың нақты пайдасын біліңіз.","auth.hero.lead":"Өзіндік құн, комиссия, жеткізу және шығындар — бір есепте.",
  "reset.title":"Жаңа құпиясөз","reset.submit":"Құпиясөзді сақтау","reset.success":"Құпиясөз өзгертілді. Енді кіруге болады.","reset.error":"Сілтеме жарамсыз немесе құпиясөз талаптарға сай емес.",
  "nav.dashboard":"Шолу","nav.products":"Тауарлар","nav.orders":"Тапсырыстар","nav.abc":"ABC-талдау","nav.exports":"Экспорт","nav.integrations":"Интеграциялар","nav.expenses":"Шығындар","nav.fees":"Комиссиялар","nav.settings":"Баптаулар","nav.admin":"SaaS Admin","nav.analytics":"ТАЛДАУ","nav.management":"БАСҚАРУ","plan.trial":"Trial тарифі","plan.pilot":"Сынақ кезеңі","plan.organization":"1 ұйым",
  "header.menu":"Мәзірді ашу","header.organization":"Ұйым","header.createOrganization":"Ұйым құру","header.organizationPrompt":"Жаңа ұйымның атауы","header.organizationCreateError":"Ұйымды құру мүмкін болмады","language.ru":"Русский","language.kk":"Қазақша"
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
