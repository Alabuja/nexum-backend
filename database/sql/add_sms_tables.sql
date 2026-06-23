CREATE TABLE IF NOT EXISTS "SmsWallets" (
    "Id" uuid PRIMARY KEY,
    "UserId" text NOT NULL,
    "Balance" integer NOT NULL DEFAULT 0,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_SmsWallets_UserId"
    ON "SmsWallets" ("UserId");

CREATE TABLE IF NOT EXISTS "SmsTransactions" (
    "Id" uuid PRIMARY KEY,
    "UserId" text NOT NULL,
    "Type" integer NOT NULL,
    "Amount" integer NOT NULL,
    "BalanceAfter" integer NOT NULL,
    "Reference" text NULL,
    "Description" text NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS "IX_SmsTransactions_UserId_CreatedAt"
    ON "SmsTransactions" ("UserId", "CreatedAt");

CREATE TABLE IF NOT EXISTS "SmsMessages" (
    "Id" uuid PRIMARY KEY,
    "UserId" text NOT NULL,
    "Mode" integer NOT NULL,
    "Recipient" text NOT NULL,
    "Body" text NOT NULL,
    "ProviderMessageId" text NULL,
    "Status" text NOT NULL DEFAULT 'Pending',
    "ErrorMessage" text NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    "SentAt" timestamp with time zone NULL
);

CREATE INDEX IF NOT EXISTS "IX_SmsMessages_UserId_CreatedAt"
    ON "SmsMessages" ("UserId", "CreatedAt");

CREATE TABLE IF NOT EXISTS "SmsTopups" (
    "Id" uuid PRIMARY KEY,
    "UserId" text NOT NULL,
    "PaystackReference" text NOT NULL,
    "Tokens" integer NOT NULL,
    "AmountNaira" numeric(18, 2) NOT NULL,
    "Status" integer NOT NULL DEFAULT 0,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
    "PaidAt" timestamp with time zone NULL,
    "CreditedAt" timestamp with time zone NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_SmsTopups_PaystackReference"
    ON "SmsTopups" ("PaystackReference");

CREATE INDEX IF NOT EXISTS "IX_SmsTopups_UserId_CreatedAt"
    ON "SmsTopups" ("UserId", "CreatedAt");
