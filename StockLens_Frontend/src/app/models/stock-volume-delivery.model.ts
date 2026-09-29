export interface VolumeDeliveryPeriod {
  tradedVolume: number;
  deliveryVolume: number;
  deliveryPercentage: number;
  formattedTradedVolume: string;
  formattedDeliveryVolume: string;
}

export interface VolumeDeliveryAnalysisResponse {
  symbol: string;
  companyName: string;
  exchange: string;
  asOfDate: string;
  day: VolumeDeliveryPeriod;
  week: VolumeDeliveryPeriod;
  month: VolumeDeliveryPeriod;
}
