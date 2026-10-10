namespace BlazorShop.Application.DTOs.Admin.Settings
{
    public class StoreSettingsDto
    {
        public string StoreName { get; set; } = string.Empty;

        public string StoreSupportEmail { get; set; } = string.Empty;

        public string StoreSupportPhone { get; set; } = string.Empty;

        public string DefaultCurrency { get; set; } = "SEK";

        public string DefaultCulture { get; set; } = "en-US";

        public bool MaintenanceModeEnabled { get; set; }

        public string MaintenanceMessage { get; set; } = string.Empty;
    }
}
