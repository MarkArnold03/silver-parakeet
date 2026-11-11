import type { ButtonHTMLAttributes, ReactNode } from "react";

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & { variant?: "primary" | "secondary" };

export function Button({ variant = "primary", className = "", ...rest }: ButtonProps) {
  return <button type="button" className={`pk-button pk-button--${variant} ${className}`} {...rest} />;
}

/** The class names for something that should look like a button but isn't one, such as a link. */
export function buttonClass(variant: "primary" | "secondary" = "primary") {
  return `pk-button pk-button--${variant}`;
}

export function Card({ title, children }: { title?: string; children: ReactNode }) {
  return (
    <section className="pk-card">
      {title && <h2 className="pk-card__title">{title}</h2>}
      {children}
    </section>
  );
}

/** What we show when there is nothing to show: a short explanation and, optionally, what to do next. */
export function EmptyState({ title, text, action }: { title: string; text: string; action?: ReactNode }) {
  return (
    <div className="pk-card pk-empty">
      <div className="pk-empty__mark" aria-hidden="true">
        <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
          <circle cx="12" cy="12" r="9" />
          <path d="M12 7v5l3 2" />
        </svg>
      </div>
      <h2>{title}</h2>
      <p>{text}</p>
      {action && <div className="pk-empty__action">{action}</div>}
    </div>
  );
}

/** The bar at the bottom of the phone screen. The links come from the app, so this knows nothing about routing. */
export function BottomNav({ label, children }: { label: string; children: ReactNode }) {
  return (
    <nav className="pk-bottomnav" aria-label={label}>
      {children}
    </nav>
  );
}
