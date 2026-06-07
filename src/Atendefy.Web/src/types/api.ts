// Auth
export interface LoginRequest {
  email: string;
  password: string;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  tenantId: string;
  userId: string;
  role: string;
}

export interface RegisterRequest {
  companyName: string;
  subdomain: string;
  ownerName: string;
  ownerEmail: string;
  ownerPassword: string;
}

export interface RegisterResponse {
  id: string;
  subdomain: string;
  name: string;
}

// WhatsApp
export interface WhatsAppAccount {
  id: string;
  provider: string;
  phone: string;
  status: string;
  createdAt: string;
}

export interface CreateWhatsAppAccountRequest {
  provider: string;
  phone: string;
  configJson: string;
}

// AI
export interface AIConfigResponse {
  provider: string;
  model: string;
  systemPrompt: string;
}

export interface AIConfigRequest {
  provider: string;
  apiKey?: string;
  model: string;
  systemPrompt: string;
}

// Billing
export interface Plan {
  id: string;
  name: string;
  priceMonthly: number;
  priceYearly: number;
  limitsJson: string;
}

export interface PlanLimits {
  messages_per_month: number;
  whatsapp_accounts: number;
  team_members: number;
}

export interface CreateSubscriptionRequest {
  planId: string;
  provider: string;       // "asaas" | "stripe"
  billingType: string;    // "BOLETO" | "PIX" | "CREDIT_CARD"
  billingCycle: string;   // "monthly" | "yearly"
  cpfCnpj?: string;
  paymentMethodId?: string;
}

export interface InvoiceResult {
  id: string;
  status: string;
  boletoUrl?: string;
  boletoBarcode?: string;
  pixCopyPaste?: string;
  clientSecret?: string;
  dueDate: string;
}

export interface SubscriptionResponse {
  id: string;
  status: string;
  billingCycle: string;
  provider: string;
  currentPeriodStart?: string;
  currentPeriodEnd?: string;
  plan?: { id: string; name: string };
  lastInvoice?: {
    id: string;
    status: string;
    amount: number;
    dueDate: string;
    paidAt?: string;
  };
}

// Conversations
export interface ConversationSummary {
  id: string;
  contactPhone: string;
  messageCount: number;
  startedAt: string;
  lastMessageAt: string;
}

export interface ConversationMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  tokensUsed: number;
  createdAt: string;
}

export interface ConversationDetail {
  id: string;
  contactPhone: string;
  startedAt: string;
  messageCount: number;
  messages: ConversationMessage[];
}

export interface ConversationsListResponse {
  conversations: ConversationSummary[];
  total: number;
  page: number;
  pageSize: number;
}
