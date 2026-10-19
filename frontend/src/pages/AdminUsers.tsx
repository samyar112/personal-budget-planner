import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { ApiError } from "../api/auth";
import { fetchAdminUsers, setUserActive, type AdminUserList } from "../api/admin";
import BrandName from "../components/BrandName";
import AmbientBackground from "../components/AmbientBackground";
import "./Home.css";

type Tab = "all" | "unverified";

const PAGE_SIZE = 50;

const AdminUsers = () => {
  const [tab, setTab] = useState<Tab>("all");
  const [page, setPage] = useState(1);
  const [reloadKey, setReloadKey] = useState(0);
  const [list, setList] = useState<AdminUserList | null>(null);
  const [error, setError] = useState("");
  // Loading is derived: a request is in flight until its key has been answered.
  const [loadedKey, setLoadedKey] = useState("");
  // Deactivating ends a person's sessions, so it takes a second click.
  const [confirmingId, setConfirmingId] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const requestKey = `${tab}|${page}|${reloadKey}`;
  const loading = loadedKey !== requestKey;

  useEffect(() => {
    let cancelled = false;
    fetchAdminUsers({ verified: tab === "unverified" ? false : undefined, page, pageSize: PAGE_SIZE })
      .then((result) => {
        if (cancelled) return;
        setList(result);
        setError("");
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof ApiError ? err.message : "Could not load users. Please try again.");
      })
      .finally(() => {
        if (!cancelled) setLoadedKey(requestKey);
      });

    return () => {
      cancelled = true;
    };
  }, [tab, page, reloadKey, requestKey]);

  const switchTab = (next: Tab) => {
    setTab(next);
    setPage(1);
    setConfirmingId(null);
  };

  const toggleActive = async (id: string, active: boolean) => {
    setBusyId(id);
    setError("");
    try {
      await setUserActive(id, active);
      setConfirmingId(null);
      setReloadKey((key) => key + 1);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not update the account. Please try again.");
    } finally {
      setBusyId(null);
    }
  };

  const totalPages = list ? Math.max(1, Math.ceil(list.total / list.pageSize)) : 1;

  return (
    <div className="home-page">
      <AmbientBackground />
      <nav className="navbar px-4 py-3 finance-navbar">
        <div className="container-fluid">
          <Link className="navbar-brand fw-bold" to="/home">
            <BrandName logoSize="sm" />
          </Link>
          <Link className="btn btn-secondary btn-sm text-nav" to="/home">
            Back to dashboard
          </Link>
        </div>
      </nav>

      <div className="container py-5">
        <div className="card glass-card dashboard-card">
          <div className="card-body">
            <h4 className="mb-1">Users</h4>
            <p className="card-section-sub mb-3">
              Accounts only. Admins cannot see anyone&apos;s financial data.
            </p>

            <ul className="nav nav-tabs mb-3">
              <li className="nav-item">
                <button
                  type="button"
                  className={`nav-link ${tab === "all" ? "active" : ""}`}
                  onClick={() => switchTab("all")}
                >
                  All users
                </button>
              </li>
              <li className="nav-item">
                <button
                  type="button"
                  className={`nav-link ${tab === "unverified" ? "active" : ""}`}
                  onClick={() => switchTab("unverified")}
                >
                  Unverified email
                </button>
              </li>
            </ul>

            {error && (
              <div className="alert alert-danger" role="alert">
                {error}
              </div>
            )}

            <div className="table-responsive">
              <table className="table align-middle">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Email</th>
                    <th>Role</th>
                    <th>Status</th>
                    <th>Created</th>
                    <th className="text-end">Action</th>
                  </tr>
                </thead>
                <tbody>
                  {loading && !list && (
                    <tr>
                      <td colSpan={6}>Loading…</td>
                    </tr>
                  )}
                  {list?.items.length === 0 && (
                    <tr>
                      <td colSpan={6}>
                        {tab === "unverified" ? "Every account has a verified email." : "No users yet."}
                      </td>
                    </tr>
                  )}
                  {list?.items.map((user) => (
                    <tr key={user.id}>
                      <td>{user.name}</td>
                      <td>
                        {user.email}
                        {!user.emailVerified && (
                          <span className="badge text-bg-warning ms-2">Unverified</span>
                        )}
                      </td>
                      <td>{user.role}</td>
                      <td>
                        {user.isActive ? (
                          <span className="badge text-bg-success">Active</span>
                        ) : (
                          <span className="badge text-bg-secondary">Deactivated</span>
                        )}
                        {user.isLocked && <span className="badge text-bg-danger ms-1">Locked</span>}
                      </td>
                      <td>{new Date(user.createdAt).toLocaleDateString()}</td>
                      <td className="text-end">
                        {user.role === "Admin" ? (
                          <span className="text-body-secondary small">—</span>
                        ) : !user.isActive ? (
                          <button
                            type="button"
                            className="btn btn-sm btn-primary"
                            disabled={busyId === user.id}
                            onClick={() => void toggleActive(user.id, true)}
                          >
                            Activate
                          </button>
                        ) : confirmingId === user.id ? (
                          <span className="d-inline-flex gap-2">
                            <button
                              type="button"
                              className="btn btn-sm btn-danger"
                              disabled={busyId === user.id}
                              onClick={() => void toggleActive(user.id, false)}
                            >
                              Confirm
                            </button>
                            <button
                              type="button"
                              className="btn btn-sm btn-secondary"
                              onClick={() => setConfirmingId(null)}
                            >
                              Cancel
                            </button>
                          </span>
                        ) : (
                          <button
                            type="button"
                            className="btn btn-sm btn-outline-danger"
                            onClick={() => setConfirmingId(user.id)}
                          >
                            Deactivate
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {list && list.total > 0 && (
              <div className="d-flex justify-content-between align-items-center">
                <span className="small text-body-secondary">
                  {list.total} {list.total === 1 ? "account" : "accounts"} · page {page} of {totalPages}
                </span>
                <span className="d-inline-flex gap-2">
                  <button
                    type="button"
                    className="btn btn-sm btn-secondary"
                    disabled={page <= 1 || loading}
                    onClick={() => setPage((current) => current - 1)}
                  >
                    Previous
                  </button>
                  <button
                    type="button"
                    className="btn btn-sm btn-secondary"
                    disabled={page >= totalPages || loading}
                    onClick={() => setPage((current) => current + 1)}
                  >
                    Next
                  </button>
                </span>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default AdminUsers;
