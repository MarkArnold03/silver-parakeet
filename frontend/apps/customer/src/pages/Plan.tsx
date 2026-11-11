import { ApiError } from "@parakeet/api";
import { Button, buttonClass, EmptyState } from "@parakeet/ui";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { deliveryKey, slotsKey, useDelivery } from "../App";
import { SlotPicker } from "../SlotPicker";
import { useI18n, type Texts } from "../texts";

/** The API's error code decides the message. Anything we don't know gets the general one. */
function refusalText(error: unknown, t: Texts): string {
  const code = error instanceof ApiError ? error.code : undefined;
  if (code === "SlotFull" || code === "ZoneDayFull" || code === "SlotClosed") return t.plan.taken;
  if (code === "CutoffPassed") return t.plan.tooLate;
  return t.plan.failed;
}

export function Plan() {
  const { api, delivery } = useDelivery();
  const { t } = useI18n();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const slots = useQuery({ queryKey: slotsKey, queryFn: api.slots, enabled: delivery.canChangeSlot });

  const book = useMutation({
    mutationFn: api.book,
    onSuccess: (updated) => {
      // The answer is the updated delivery, so Home can show it without asking again.
      queryClient.setQueryData(deliveryKey, updated);
      void queryClient.invalidateQueries({ queryKey: slotsKey });
      void navigate("..", { relative: "path", state: { booked: true } });
    },
    onError: () => {
      // Most likely someone else took the time. Fetch the list again so it is gone from the screen.
      setSelectedId(null);
      void queryClient.invalidateQueries({ queryKey: slotsKey });
    },
  });

  if (!delivery.canChangeSlot) {
    return (
      <EmptyState
        title={t.plan.lockedTitle}
        text={t.plan.lockedText}
        action={
          <Link className={buttonClass("secondary")} to=".." relative="path">
            {t.plan.backHome}
          </Link>
        }
      />
    );
  }
  if (slots.isPending) {
    return <p role="status">{t.loading}</p>;
  }
  if (slots.isError) {
    return (
      <EmptyState title={t.error.title} text={t.error.text} action={<Button onClick={() => slots.refetch()}>{t.retry}</Button>} />
    );
  }

  // The time already booked is offered by the API too (you may keep it). Booking it again changes nothing.
  const selectable = slots.data.filter((slot) => slot.id !== delivery.slot?.id || delivery.status === "Failed");

  return (
    <>
      <h2 className="page__title">{t.plan.title}</h2>
      {book.isError && (
        <p className="notice notice--warn" role="alert">
          {refusalText(book.error, t)}
        </p>
      )}
      <SlotPicker slots={selectable} selectedId={selectedId} onSelect={setSelectedId} />
      {selectable.length > 0 && (
        <div className="confirm">
          <Button disabled={selectedId === null || book.isPending} onClick={() => selectedId && book.mutate(selectedId)}>
            {book.isPending ? t.plan.saving : t.plan.confirm}
          </Button>
        </div>
      )}
    </>
  );
}
