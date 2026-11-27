import { Component, type ErrorInfo, type ReactNode } from "react";

type Props = { children: ReactNode };
type State = { failed: boolean };

/**
 * Last line of defence: a render crash shows a recovery screen instead of a blank page. It only sees errors
 * thrown while rendering, including a lazy page chunk that fails to load after a deploy. Failed API calls are
 * async and never reach it; pages show those inline. No error text is shown, so nothing internal leaks.
 */
class ErrorBoundary extends Component<Props, State> {
  state: State = { failed: false };

  static getDerivedStateFromError(): State {
    return { failed: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // Browser console only; nothing is sent anywhere.
    console.error("Render failed", error, info.componentStack);
  }

  render() {
    if (!this.state.failed) {
      return this.props.children;
    }

    return (
      <div className="d-flex min-vh-100 align-items-center justify-content-center p-3">
        <div className="glass-panel p-4 text-center" role="alert" style={{ maxWidth: 420 }}>
          <h1 className="h5 mb-2">Something went wrong on this page</h1>
          <p className="mb-3 text-body-secondary">Your data is safe. Reloading usually fixes it.</p>
          <div className="d-flex gap-2 justify-content-center">
            <button type="button" className="btn btn-primary" onClick={() => window.location.reload()}>
              Reload
            </button>
            {/* A plain link, not router navigation: the router itself may be what failed. */}
            <a className="btn btn-outline-secondary" href="/home">
              Go to dashboard
            </a>
          </div>
        </div>
      </div>
    );
  }
}

export default ErrorBoundary;
