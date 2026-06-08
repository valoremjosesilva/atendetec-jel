export const ConversationStatus = {
  OPEN: 'open',
  RESOLVED: 'resolved',
  ALL: 'all',
} as const;

export const MessageRole = {
  USER: 'user',
  ASSISTANT: 'assistant',
  AGENT: 'agent',
} as const;

export const WhatsAppStatus = {
  CONNECTED: 'connected',
  OPEN: 'open',
  CLOSE: 'close',
  CONNECTING: 'connecting',
  DISCONNECTED: 'disconnected',
} as const;

export const WhatsAppProvider = {
  META: 'meta',
  EVOLUTION: 'evolution',
} as const;

export const BillingProvider = {
  ASAAS: 'asaas',
  STRIPE: 'stripe',
} as const;

export const BillingType = {
  BOLETO: 'BOLETO',
  PIX: 'PIX',
  CREDIT_CARD: 'CREDIT_CARD',
} as const;

export const BillingCycle = {
  MONTHLY: 'monthly',
  YEARLY: 'yearly',
} as const;

export const SubscriptionStatus = {
  PENDING: 'pending',
  ACTIVE: 'active',
  PAST_DUE: 'past_due',
  SUSPENDED: 'suspended',
  CANCELLED: 'cancelled',
} as const;

export const InvoiceStatus = {
  PENDING: 'pending',
  PAID: 'paid',
  OVERDUE: 'overdue',
  CANCELLED: 'cancelled',
} as const;

export const AiProvider = {
  OPENAI: 'openai',
  ANTHROPIC: 'anthropic',
  MOCK: 'mock',
} as const;
