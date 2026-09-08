export interface ScraperSettings {
  configured: boolean;
  developerConfigured: boolean;
  userConfigured: boolean;
  regionPriority: string[];
  languagePriority: string[];
  requestTimeoutSeconds: number;
  maxDownloadMegabytes: number;
}
