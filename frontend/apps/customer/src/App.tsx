import { ApiError, createCustomerApi, type CustomerApi, type CustomerDelivery } from "@parakeet/api";
import { BottomNav, Button, EmptyState } from "@parakeet/ui";
import { useQuery } from "@tanstack/react-query";
import { useMemo } from "react";
import { BrowserRouter, NavLink, Outlet, Route, Routes, useOutletContext, useParams } from "react-router";
import { Home } from "./pages/Home";
import { Plan } from "./pages/Plan";
import { useI18n } from "./texts";

export function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/b/:token" element={<DeliveryLayout />}>
          <Route index element={<Home />} />
          <Route path="plan" element={<Plan />} />
        </Route>
        <Route path="*" element={<NoLink />} />
      </Routes>
    </BrowserRouter>
  );
}

/** What every page under /b/:token gets: the API (already carrying the token) and the delivery. */
export type DeliveryContext = { api: CustomerApi; delivery: CustomerDelivery };
export const useDelivery = () => useOutletContext<DeliveryContext>();
export const deliveryKey = ["delivery"] as const;
export const slotsKey = ["slots"] as const;

function DeliveryLayout() {
  const { token = "" } = useParams();
  const { t } = useI18n();
  // The token only lives in the address bar and in memory. It is never saved on the phone.
  const api = useMemo(() => createCustomerApi(token), [token]);
  const delivery = useQuery({ queryKey: deliveryKey, queryFn: api.delivery });

  if (delivery.isPending) {
    return <Page>{<p role="status">{t.loading}</p>}</Page>;
  }
  if (delivery.isError) {
    const linkProblem = delivery.error instanceof ApiError && delivery.error.status === 401;
    return (
      <Page>
        {linkProblem ? (
          <EmptyState title={t.link.invalidTitle} text={t.link.invalidText} />
        ) : (
          <EmptyState
            title={t.error.title}
            text={t.error.text}
            action={<Button onClick={() => delivery.refetch()}>{t.retry}</Button>}
          />
        )}
      </Page>
    );
  }

  return (
    <Page orderNumber={delivery.data.orderNumber}>
      <Outlet context={{ api, delivery: delivery.data } satisfies DeliveryContext} />
      <BottomNav label={t.nav.label}>
        <NavLink to="." end>
          {t.nav.home}
        </NavLink>
        <NavLink to="plan">{t.nav.plan}</NavLink>
      </BottomNav>
    </Page>
  );
}

function NoLink() {
  const { t } = useI18n();
  return (
    <Page>
      <EmptyState title={t.link.missingTitle} text={t.link.missingText} />
    </Page>
  );
}

function Page({ orderNumber, children }: { orderNumber?: number; children: React.ReactNode }) {
  const { t, lang, setLang } = useI18n();
  return (
    <div className="page" lang={lang}>
      <header className="page__header">
        <div>
          <h1>{t.appTitle}</h1>
          {orderNumber !== undefined && <p>{t.orderNumber(orderNumber)}</p>}
        </div>
        <button type="button" className="page__lang" onClick={() => setLang(lang === "sv" ? "en" : "sv")}>
          {t.switchLanguage}
        </button>
      </header>
      <main className="page__main">{children}</main>
    </div>
  );
}
