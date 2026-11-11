import type { AvailableSlot } from "@parakeet/api";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { SlotPicker } from "./SlotPicker";
import { I18nProvider } from "./texts";

const slot = (id: string, date: string, start: string, end: string): AvailableSlot => ({ id, date, start, end });

function show(slots: AvailableSlot[], lang: "sv" | "en" = "sv", selectedId: string | null = null) {
  const onSelect = vi.fn();
  render(
    <I18nProvider initial={lang}>
      <SlotPicker slots={slots} selectedId={selectedId} onSelect={onSelect} />
    </I18nProvider>,
  );
  return onSelect;
}

describe("SlotPicker", () => {
  it("explains that there are no times when the list is empty", () => {
    show([]);

    expect(screen.getByRole("heading", { name: "Inga lediga tider just nu" })).toBeInTheDocument();
    expect(screen.queryByRole("radio")).not.toBeInTheDocument();
  });

  it("shows the empty state in English too", () => {
    show([], "en");

    expect(screen.getByRole("heading", { name: "No times available right now" })).toBeInTheDocument();
  });

  it("groups the times by day, in order, even if the API sends them mixed", () => {
    show([
      slot("c", "2026-11-13", "08:00:00", "12:00:00"),
      slot("b", "2026-11-12", "12:00:00", "16:00:00"),
      slot("a", "2026-11-12", "08:00:00", "12:00:00"),
    ]);

    const days = screen.getAllByRole("group");
    expect(days.map((day) => within(day).getAllByRole("radio").length)).toEqual([2, 1]);
    expect(days[0]).toHaveAccessibleName("torsdag 12 november");
    expect(within(days[0]!).getAllByRole("radio").map((radio) => radio.parentElement?.textContent)).toEqual([
      "08:00–12:00",
      "12:00–16:00",
    ]);
  });

  it("tells the page which time was picked and marks the selected one", async () => {
    const onSelect = show([slot("a", "2026-11-12", "08:00:00", "12:00:00"), slot("b", "2026-11-12", "12:00:00", "16:00:00")], "sv", "a");

    expect(screen.getByRole("radio", { name: "08:00–12:00" })).toBeChecked();
    await userEvent.click(screen.getByRole("radio", { name: "12:00–16:00" }));

    expect(onSelect).toHaveBeenCalledWith("b");
  });
});
