import type { AvailableSlot } from "@parakeet/api";
import type { Lang } from "@parakeet/ui";

const locale = (lang: Lang) => (lang === "sv" ? "sv-SE" : "en-GB");

/**
 * The API sends a plain date like "2026-11-12" (a day in Sweden, no time zone). Building the Date
 * from its parts keeps it on that day whatever time zone the phone is in.
 */
function toDate(isoDate: string): Date {
  const [year = 0, month = 1, day = 1] = isoDate.split("-").map(Number);
  return new Date(year, month - 1, day);
}

/** "torsdag 12 november" / "Thursday 12 November" */
export function formatDay(isoDate: string, lang: Lang): string {
  return new Intl.DateTimeFormat(locale(lang), { weekday: "long", day: "numeric", month: "long" }).format(toDate(isoDate));
}

/** "08:00:00" → "08:00" */
export const formatTime = (time: string) => time.slice(0, 5);

export const formatWindow = (slot: Pick<AvailableSlot, "start" | "end">) =>
  `${formatTime(slot.start)}–${formatTime(slot.end)}`;

/** Slots for the same day together, days in order, times in order within a day. */
export function groupByDay(slots: AvailableSlot[]): { date: string; slots: AvailableSlot[] }[] {
  const days = new Map<string, AvailableSlot[]>();
  for (const slot of [...slots].sort((a, b) => (a.date + a.start).localeCompare(b.date + b.start))) {
    days.set(slot.date, [...(days.get(slot.date) ?? []), slot]);
  }
  return [...days].map(([date, daySlots]) => ({ date, slots: daySlots }));
}
