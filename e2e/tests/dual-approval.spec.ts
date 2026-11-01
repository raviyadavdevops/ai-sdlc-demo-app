import { expect, test, type APIRequestContext } from '@playwright/test';
import { InvoiceApprovalPage } from '../pages/InvoiceApprovalPage';

// Demo-only accounts from backend/src/InvoiceApp.Api/appsettings.json.
const ADMIN = { username: 'demo.admin', password: 'ChangeMe123!' };
const MANAGER = { username: 'demo.manager', password: 'ChangeMe123!' };
const DIRECTOR = { username: 'demo.director', password: 'ChangeMe123!' };

const DUAL_APPROVAL_AMOUNT = 600_000;

async function signInViaApi(request: APIRequestContext, account: typeof ADMIN): Promise<string> {
  const response = await request.post('/api/auth/login', { data: account });
  expect(response.ok()).toBeTruthy();
  return (await response.json()).token as string;
}

async function adminHeaders(request: APIRequestContext) {
  return { Authorization: `Bearer ${await signInViaApi(request, ADMIN)}` };
}

// Arrange only: create and submit an invoice so it is waiting for approval.
async function createPendingInvoice(request: APIRequestContext, amount: number): Promise<string> {
  const headers = await adminHeaders(request);
  const customerName = `E2E dual approval ${Date.now()}`;

  const created = await request.post('/api/invoices', { headers, data: { customerName, amount } });
  expect(created.ok()).toBeTruthy();
  const { id } = await created.json();

  const submitted = await request.post(`/api/invoices/${id}/submit`, { headers });
  expect(submitted.ok()).toBeTruthy();
  return id as string;
}

async function fetchInvoice(request: APIRequestContext, invoiceId: string) {
  const response = await request.get(`/api/invoices/${invoiceId}`, { headers: await adminHeaders(request) });
  expect(response.ok()).toBeTruthy();
  return response.json();
}

test.describe('dual approval above 500,000', () => {
  let invoiceId: string;

  test.beforeEach(async ({ request }) => {
    // A fresh invoice per test, so this spec never depends on or disturbs other specs' data.
    invoiceId = await createPendingInvoice(request, DUAL_APPROVAL_AMOUNT);
  });

  test.afterEach(async ({ request }) => {
    // Remove only the invoice this test created (Admin-only endpoint).
    await request.delete(`/api/invoices/${invoiceId}`, { headers: await adminHeaders(request) });
  });

  test('needs a second approval from a different Director before the invoice is Approved', async ({ page, request }) => {
    const approvals = new InvoiceApprovalPage(page);

    // First approval: a Manager. The invoice must stay pending and show who approved.
    await approvals.open();
    await approvals.signIn(MANAGER.username, MANAGER.password);
    await expect(approvals.signedInAs(MANAGER.username, 'Manager')).toBeVisible();
    await approvals.openApprovalsTab();
    await approvals.approve(invoiceId);

    await expect(approvals.secondApprovalNotice(invoiceId)).toBeVisible();
    await expect(approvals.pendingItem(invoiceId)).toContainText('demo.manager (Manager)');
    await expect(approvals.decisionError()).toBeHidden();
    await approvals.openInvoicesTab();
    await expect(approvals.invoiceRow(invoiceId)).toContainText('Pending Approval');
    await approvals.openApprovalsTab();

    // The same Manager cannot approve again; the server's reason is shown and nothing changes.
    await approvals.approve(invoiceId);
    await expect(approvals.decisionError()).toHaveText(
      'demo.manager has already approved this invoice; a different approver is required.',
    );
    await expect(approvals.secondApprovalNotice(invoiceId)).toBeVisible();

    const afterDuplicate = await fetchInvoice(request, invoiceId);
    expect(afterDuplicate.status).toBe('PendingApproval');
    expect(afterDuplicate.decidedBy).toBeNull();
    expect(afterDuplicate.decidedAt).toBeNull();
    expect(afterDuplicate.approvals).toHaveLength(1);
    expect(afterDuplicate.approvals[0]).toMatchObject({ approver: 'demo.manager', role: 'Manager' });

    // Second approval: a genuinely different user (new sign-in, new session) who is a Director.
    await approvals.signOut();
    await approvals.signIn(DIRECTOR.username, DIRECTOR.password);
    await expect(approvals.signedInAs(DIRECTOR.username, 'Director')).toBeVisible();
    await approvals.openApprovalsTab();
    await expect(approvals.secondApprovalNotice(invoiceId)).toBeVisible();
    await approvals.approve(invoiceId);

    // The invoice leaves the pending list and is shown as Approved.
    await expect(approvals.pendingItem(invoiceId)).toHaveCount(0);
    await approvals.openInvoicesTab();
    await expect(approvals.invoiceRow(invoiceId)).toContainText('Approved');
    await expect(approvals.invoiceRow(invoiceId)).not.toContainText('Pending Approval');

    // The server recorded both distinct approvals and decided with the second approver.
    const stored = await fetchInvoice(request, invoiceId);
    expect(stored.status).toBe('Approved');
    expect(stored.decidedBy).toBe('demo.director');
    expect(stored.decidedAt).not.toBeNull();
    expect(stored.approvals).toHaveLength(2);
    expect(stored.approvals[0]).toMatchObject({ approver: 'demo.manager', role: 'Manager' });
    expect(stored.approvals[1]).toMatchObject({ approver: 'demo.director', role: 'Director' });
  });
});
