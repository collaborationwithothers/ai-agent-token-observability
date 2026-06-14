import "./App.css";

export function App() {
  return (
    <main className="dashboard-shell">
      <section className="dashboard-panel">
        <p className="eyebrow">Azure Production MVP</p>
        <h1>AI Agent Token Observability</h1>
        <dl className="status-list" aria-label="Token Observability runtime status">
          <div>
            <dt>Dashboard</dt>
            <dd>Online</dd>
          </div>
          <div>
            <dt>Product API</dt>
            <dd>Pending connection</dd>
          </div>
        </dl>
      </section>
    </main>
  );
}
