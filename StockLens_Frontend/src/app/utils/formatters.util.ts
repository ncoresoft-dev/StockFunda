/**
 * Common formatting utilities for StockLens financial and numeric displays.
 * Follows Indian numbering system ('en-IN') standards.
 */

/**
 * Formats a number with Indian comma separation and fixed or flexible decimals.
 * e.g., 1234567.89 -> "12,34,567.89"
 */
export function formatNumber(
  val?: number | null,
  decimals: number = 2,
  fallback: string = '—',
  minDecimals?: number
): string {
  if (val === null || val === undefined || isNaN(val)) {
    return fallback;
  }
  const minDigits = minDecimals !== undefined ? minDecimals : (decimals === 0 ? 0 : decimals);
  return Number(val).toLocaleString('en-IN', {
    minimumFractionDigits: minDigits,
    maximumFractionDigits: decimals
  });
}

/**
 * Formats a percentage value with optional +/- sign.
 * e.g., 4.5 -> "+4.50%", -2.1 -> "-2.10%"
 */
export function formatPercent(
  val?: number | null,
  decimals: number = 2,
  withSign: boolean = true,
  fallback: string = '—'
): string {
  if (val === null || val === undefined || isNaN(val)) {
    return fallback;
  }
  const num = Number(val);
  const prefix = withSign && num > 0 ? '+' : '';
  return `${prefix}${num.toFixed(decimals)}%`;
}

/**
 * Formats a percentage without explicit positive sign.
 * e.g., 15.25 -> "15.25%"
 */
export function formatPercentage(
  val?: number | null,
  decimals: number = 2,
  fallback: string = '—'
): string {
  return formatPercent(val, decimals, false, fallback);
}

/**
 * Formats a currency value with ₹ symbol.
 * Handles negative values cleanly (e.g., -₹2,500.50).
 */
export function formatPrice(
  val?: number | null,
  decimals: number = 2,
  fallback: string = '—',
  minDecimals?: number
): string {
  if (val === null || val === undefined || isNaN(val)) {
    return fallback;
  }
  const num = Number(val);
  const isNeg = num < 0;
  const absVal = Math.abs(num);
  const minDigits = minDecimals !== undefined ? minDecimals : (decimals === 0 ? 0 : decimals);
  return `${isNeg ? '-' : ''}₹${absVal.toLocaleString('en-IN', {
    minimumFractionDigits: minDigits,
    maximumFractionDigits: decimals
  })}`;
}

/**
 * Alias for formatPrice.
 */
export function formatCurrency(
  val?: number | null,
  decimals: number = 2,
  fallback: string = '—',
  minDecimals?: number
): string {
  return formatPrice(val, decimals, fallback, minDecimals);
}

/**
 * Formats values reported in ₹ Crores.
 * e.g., 1450.25 -> "₹1,450.25 Cr", -1450 -> "-₹1,450 Cr"
 */
export function formatCrores(
  val?: number | null,
  decimals: number = 2,
  fallback: string = '—',
  minDecimals?: number,
  suffix: string = ' Cr'
): string {
  if (val === null || val === undefined || isNaN(val)) {
    return fallback;
  }
  const num = Number(val);
  const isNeg = num < 0;
  const absVal = Math.abs(num);
  const minDigits = minDecimals !== undefined ? minDecimals : (decimals === 0 ? 0 : decimals);
  return `${isNeg ? '-' : ''}₹${absVal.toLocaleString('en-IN', {
    minimumFractionDigits: minDigits,
    maximumFractionDigits: decimals
  })}${suffix}`;
}
