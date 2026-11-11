import "@parakeet/ui/tokens.css";
import "./app.css";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { ApiError } from "@parakeet/api";
import { App } from "./App";
import { I18nProvider } from "./texts";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // A refused request (bad link, not found) won't get better by asking again. Network trouble might.
      retry: (failures, error) => !(error instanceof ApiError && error.status < 500) && failures < 2,
    },
  },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <I18nProvider>
        <App />
      </I18nProvider>
    </QueryClientProvider>
  </StrictMode>,
);
