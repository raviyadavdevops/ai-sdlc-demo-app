import { expect, test, type APIRequestContext } from '@playwright/test';
import { InvoiceApprovalPage } from '../pages/InvoiceApprovalPage';

// Demo-only accounts from backend/src/InvoiceApp.Api/appsettings.json.
const ADMIN = { username: 'demo.admin', password: 'ChangeMe123!' };
const MANAGER = { username: 'demo.manager', password: 'ChangeMe123!' };

const OVER_MANAGER_LIMIT = 75_000;

async function signInViaApi(request: APIRequestContext, account: typeof ADMIN): Promise<string> {
  const response = await request.post('/api/auth/login', { data: account });
  expect(response.ok()).toBeTruthy();
  return (await response.json()).token as string;
}

// Arrange only: create and submit an invoice so it is waiting for approval.
async function createPendingInvoice(request: APIRequestContext, amount: number): Promise<string> {
  const headers = { Authorization: `Bearer ${await signInViaApi(request, ADMIN)}` };
  const customerName = `E2E over-limit ${Date.now()}`;

  const created = await request.post('/api/invoices', { headers, data: { customerName, amount } });
  expect(created.ok()).toBeTruthy();
  const { id } = await created.json();

  const submitted = await request.post(`/api/invoices/${id}/submit`, { headers });
  expect(submitted.ok()).toBeTruthy();
  return id as string;
}

test('a Manager is refused with a clear reason when approving above 50,000, and the invoice is unchanged', async ({ page, request }) => {
  const invoiceId = await createPendingInvoice(request, OVER_MANAGER_LIMIT);
  const approvals = new InvoiceApprovalPage(page);

  await approvals.open();
  await approvals.signIn(MANAGER.username, MANAGER.password);
  await approvals.openApprovalsTab();
  await approvals.approve(invoiceId);

  await expect(approvals.decisionError()).toHaveText(
    'Manager approval limit is 50,000; invoice amount is 75,000.',
  );

  // The invoice is still waiting for approval in the UI...
  await expect(approvals.pendingApprovalButton(invoiceId)).toBeVisible();
  await approvals.openInvoicesTab();
  await expect(approvals.invoiceRow(invoiceId)).toContainText('Pending Approval');

  // ...and the server holds no decision or approval for it.
  const adminHeaders = { Authorization: `Bearer ${await signInViaApi(request, ADMIN)}` };
  const stored = await (await request.get(`/api/invoices/${invoiceId}`, { headers: adminHeaders })).json();
  expect(stored.status).toBe('PendingApproval');
  expect(stored.decidedBy).toBeNull();
  expect(stored.decidedAt).toBeNull();
  expect(stored.approvals).toEqual([]);
});
