Backup of customer detail design before CRM redesign (2026-07-18).
To restore:
  cp Views/Customers/_backups/Details.pre-crm-20260718.cshtml Views/Customers/Details.cshtml
  # Optional: restore CSS only if you also want to remove CRM styles from site.css
  # cp Views/Customers/_backups/site.pre-crm-20260718.css wwwroot/css/site.css
