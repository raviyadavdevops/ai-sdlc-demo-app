import { expect, type Locator, type Page } from '@playwright/test';

/** Page object for signing in and deciding pending invoices in the Invoice Approvals UI. */
export class InvoiceApprovalPage {
  constructor(private readonly page: Page) {}

  async open(): Promise<void> {
    await this.page.goto('/');
  }

  async signIn(username: string, password: string): Promise<void> {
    await this.page.getByLabel('Username').fill(username);
    await this.page.getByLabel('Password').fill(password);
    await this.page.getByRole('button', { name: 'Sign in' }).click();
    await expect(this.page.getByRole('button', { name: 'Sign out' })).toBeVisible();
  }

  async signOut(): Promise<void> {
    await this.page.getByRole('button', { name: 'Sign out' }).click();
    await expect(this.page.getByRole('button', { name: 'Sign in' })).toBeVisible();
  }

  /** The signed-in user shown in the header, e.g. "demo.manager (Manager)". */
  signedInAs(username: string, role: string): Locator {
    return this.page.getByText(`${username} (${role})`, { exact: true });
  }

  /** The entry for one invoice in the pending-approvals list. */
  pendingItem(invoiceId: string): Locator {
    return this.page.getByRole('listitem').filter({ hasText: invoiceId });
  }

  /** The note shown on a pending invoice that is waiting for its second approval. */
  secondApprovalNotice(invoiceId: string): Locator {
    return this.pendingItem(invoiceId).getByText('Second approval by a Director required');
  }

  async openApprovalsTab(): Promise<void> {
    await this.page.getByRole('tab', { name: 'Approvals' }).click();
  }

  async openInvoicesTab(): Promise<void> {
    await this.page.getByRole('tab', { name: 'Invoices' }).click();
  }

  async approve(invoiceId: string): Promise<void> {
    await this.page.getByRole('button', { name: `Approve invoice ${invoiceId}` }).click();
  }

  pendingApprovalButton(invoiceId: string): Locator {
    return this.page.getByRole('button', { name: `Approve invoice ${invoiceId}` });
  }

  /** The visible alert that carries the server's reason for refusing a decision. */
  decisionError(): Locator {
    return this.page.getByRole('alert');
  }

  invoiceRow(invoiceId: string): Locator {
    return this.page.getByRole('row').filter({ hasText: invoiceId });
  }
}
