import { buttonClass, Card } from "@parakeet/ui";
import { useEffect, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router";
import { useDelivery } from "../App";
import { formatDay, formatWindow } from "../dates";
import { useI18n } from "../texts";

export function Home() {
  const { delivery } = useDelivery();
  const { t, lang } = useI18n();
  const location = useLocation();
  const navigate = useNavigate();
  // The Plan page sends "booked" along when it brings the customer back here. We remember it for this
  // visit and then wipe it from the browser's history, so a reload doesn't congratulate them again.
  const [justBooked] = useState((location.state as { booked?: boolean } | null)?.booked === true);
  useEffect(() => {
    if (location.state) void navigate(".", { replace: true, state: null });
  }, [location.state, navigate]);
  // A failed attempt keeps its old slot for history. To the customer it is simply "no time right now".
  const slot = delivery.status === "Failed" ? null : delivery.slot;

  return (
    <>
      {justBooked && (
        <p className="notice notice--ok" role="status">
          {t.home.booked}
        </p>
      )}

      <section className="hero">
        <p className="hero__status">{t.status[delivery.status]}</p>
        {slot ? (
          <>
            <h2>{t.home.yourTime}</h2>
            <p className="hero__day">{formatDay(slot.date, lang)}</p>
            <p className="hero__time">{formatWindow(slot)}</p>
            {delivery.canChangeSlot && (
              <Link className={buttonClass("secondary")} to="plan">
                {t.home.changeTime}
              </Link>
            )}
          </>
        ) : (
          <>
            <h2>{t.home.noTimeTitle}</h2>
            <p>{delivery.status === "Failed" ? t.home.failedText : t.home.noTimeText}</p>
            {delivery.canChangeSlot && (
              <Link className={buttonClass("primary")} to="plan">
                {t.home.chooseTime}
              </Link>
            )}
          </>
        )}
      </section>

      <Card title={t.home.address}>
        <p>{delivery.recipientName}</p>
        <p>{delivery.street}</p>
        <p>
          {delivery.postcode} {delivery.city}
        </p>
        <p className="muted">{t.deliveryType[delivery.deliveryType]}</p>
      </Card>

      <Card title={t.home.items}>
        <ul className="items">
          {delivery.lines.map((line, index) => (
            <li key={index}>
              <span>{line.description}</span>
              <span className="muted">{t.home.quantity(line.quantity)}</span>
            </li>
          ))}
        </ul>
      </Card>

      <p className="muted small">{t.home.linkValid(formatDay(delivery.linkValidThrough, lang))}</p>
    </>
  );
}
