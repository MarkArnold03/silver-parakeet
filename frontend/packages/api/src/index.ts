import createClient from "openapi-fetch";
import type { components, paths } from "./schema";

// schema.d.ts is generated from the backend's openapi.json ("pnpm gen:api"). Never edit it by hand.
export type { paths };
type Schemas = components["schemas"];
export type CustomerDelivery = Schemas["CustomerDeliveryDto"];
export type AvailableSlot = Schemas["AvailableSlot"];
export type DeliveryStatus = Schemas["DeliveryStatus"];
export type DeliveryType = Schemas["DeliveryType"];

/** A refusal from the API. `code` is the stable name the backend gives every error (SlotFull, LinkExpired, ...). */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    message: string,
  ) {
    super(message);
  }
}

async function unwrap<T>(call: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const { data, error, response } = await call;
  if (!response.ok || data === undefined) {
    const problem = (error ?? {}) as { code?: string; title?: string };
    throw new ApiError(response.status, problem.code, problem.title ?? response.statusText);
  }
  return data;
}

/** Everything the customer app can ask the API. The token is the last part of the customer's link. */
export function createCustomerApi(token: string, baseUrl = "") {
  const client = createClient<paths>({ baseUrl, headers: { Authorization: `Bearer ${token}` } });
  return {
    delivery: () => unwrap<CustomerDelivery>(client.GET("/api/customer/delivery")),
    slots: () => unwrap<AvailableSlot[]>(client.GET("/api/customer/slots")),
    book: (slotId: string) =>
      unwrap<CustomerDelivery>(client.PUT("/api/customer/delivery/slot", { body: { slotId } })),
  };
}

export type CustomerApi = ReturnType<typeof createCustomerApi>;
