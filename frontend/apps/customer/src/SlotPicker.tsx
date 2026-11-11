import type { AvailableSlot } from "@parakeet/api";
import { EmptyState } from "@parakeet/ui";
import { formatDay, formatWindow, groupByDay } from "./dates";
import { useI18n } from "./texts";

type Props = {
  slots: AvailableSlot[];
  selectedId: string | null;
  onSelect: (slotId: string) => void;
};

/**
 * The bookable times, one group per day. Real radio buttons underneath, so it works with a
 * keyboard and a screen reader without extra code. No slots is a normal case, not an error.
 */
export function SlotPicker({ slots, selectedId, onSelect }: Props) {
  const { t, lang } = useI18n();

  if (slots.length === 0) {
    return <EmptyState title={t.plan.emptyTitle} text={t.plan.emptyText} />;
  }

  return (
    <div className="slots">
      {groupByDay(slots).map((day) => (
        <fieldset key={day.date} className="slots__day">
          <legend>{formatDay(day.date, lang)}</legend>
          <div className="slots__times">
            {day.slots.map((slot) => (
              <label key={slot.id} className="slots__time">
                <input
                  type="radio"
                  name="slot"
                  value={slot.id}
                  checked={slot.id === selectedId}
                  onChange={() => onSelect(slot.id)}
                />
                <span>{formatWindow(slot)}</span>
              </label>
            ))}
          </div>
        </fieldset>
      ))}
    </div>
  );
}
