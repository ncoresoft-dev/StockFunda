export interface StockAnnouncement {
  id: number;
  seqId: string;
  symbol: string;
  companyName: string;
  isin?: string;
  industry?: string;
  subject?: string;
  details?: string;
  attachmentUrl?: string;
  attachmentFileSize?: string;
  hasXbrl: boolean;
  broadcastDateTime?: string;
  disseminationTime?: string;
  difference?: string;
  timeAgo?: string;
  createdAt: string;
}

export interface StockAnnouncementResponse {
  totalCount: number;
  page: number;
  limit: number;
  totalPages: number;
  lastSyncedAt?: string;
  categories: string[];
  announcements: StockAnnouncement[];
}
