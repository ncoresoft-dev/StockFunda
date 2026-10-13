import { Injectable } from '@angular/core';

export type DarvasState =
  | 'SearchingForTop'
  | 'ConfirmingTop'
  | 'SearchingForBottom'
  | 'ConfirmingBottom'
  | 'BoxActive'
  | 'BullishBreakout'
  | 'BearishBreakdown';

export interface DarvasBox {
  high: number;
  low: number;
  startTimestamp: number;
  endTimestamp: number;

  highIndex: number;
  lowIndex: number;

  breakoutTimestamp?: number;
  breakoutPrice?: number;
  breakoutVolume?: number;

  state: 'forming' | 'confirmed' | 'bullish_breakout' | 'bearish_breakdown';
}

export interface DarvasDetectorOptions {
  /**
   * Number of consecutive bars required to confirm a box Top or Bottom.
   * Standard Nicolas Darvas rule: 3 candles.
   */
  confirmationBars?: number;

  /**
   * Minimum lookback bars to identify a candidate swing peak when SearchingForTop.
   * Prevents treating minor downward wiggles as box tops.
   * Default: 5 bars.
   */
  lookbackForTop?: number;

  /**
   * Maximum bars a box can stay active before closing to prevent multi-month indefinite boxes.
   * Default: 35 bars (~7 calendar weeks).
   */
  maxBoxBars?: number;

  /**
   * Minimum height percentage between Top and Bottom to filter out flat noise.
   * Default: 1.0%
   */
  minBoxHeightPct?: number;

  /**
   * Maximum height percentage between Top and Bottom to filter out abnormal crash/volatility spikes.
   * Default: 40.0%
   */
  maxBoxHeightPct?: number;

  /**
   * Require candle Close to break out beyond box boundaries instead of raw intraday wick.
   * Prevents false breakouts / bull traps. Default: true.
   */
  breakoutOnClose?: boolean;
}

@Injectable({
  providedIn: 'root'
})
export class DarvasBoxDetectorService {

  /**
   * Deterministic Nicolas Darvas Box detection algorithm based on historical OHLC data.
   *
   * State Machine Flow:
   * 1. SearchingForTop: Looks for a swing high exceeding preceding lookback bars.
   * 2. ConfirmingTop: Requires 3 consecutive candles with high < candidateTop.
   *    If breached or touched (equal high), resets candidate top / countdown.
   * 3. ConfirmingBottom: Tracks lowest reaction low formed since candidateTop.
   *    Requires 3 consecutive candles with low > candidateBottom, staying strictly < candidateTop.
   * 4. BoxActive: Once both Top & Bottom are confirmed, box is active between [low, high].
   * 5. BullishBreakout: Triggered when candle closes ABOVE Box Top (or intraday high if configured).
   *    Next Darvas Box search immediately begins from the breakout high.
   * 6. BearishBreakdown: Triggered when candle closes BELOW Box Bottom.
   *    Returns to SearchingForTop.
   * 7. Max duration constraint: If price stays inside without breakout for > maxBoxBars,
   *    box concludes cleanly, allowing new consolidation ranges to form.
   */
  detectBoxes(
    dates: string[],
    opens: number[] | undefined,
    highs: number[] | undefined,
    lows: number[] | undefined,
    closes: number[],
    volumes?: number[],
    options?: DarvasDetectorOptions
  ): DarvasBox[] {
    if (!dates || dates.length < 8 || !closes || closes.length === 0) {
      return [];
    }

    const n = dates.length;
    const timestamps = dates.map(d => new Date(d).getTime());

    // Fallbacks if highs/lows are not provided or mismatched
    const hArr = highs && highs.length === n ? highs : closes;
    const lArr = lows && lows.length === n ? lows : closes;
    const vArr = volumes && volumes.length === n ? volumes : new Array(n).fill(0);

    const confirmationBars = options?.confirmationBars ?? 3;
    const lookbackForTop = options?.lookbackForTop ?? 5;
    const maxBoxBars = options?.maxBoxBars ?? 35;
    const minBoxHeightPct = options?.minBoxHeightPct ?? 1.0;
    const maxBoxHeightPct = options?.maxBoxHeightPct ?? 40.0;
    const breakoutOnClose = options?.breakoutOnClose ?? true;

    const detectedBoxes: DarvasBox[] = [];

    let state: DarvasState = 'SearchingForTop';
    let candidateTop = 0;
    let candidateTopIndex = -1;
    let topConfirmBars = 0;

    let candidateBottom = 0;
    let candidateBottomIndex = -1;
    let bottomConfirmBars = 0;

    let activeBox: DarvasBox | null = null;

    for (let i = 0; i < n; i++) {
      const curHigh = hArr[i];
      const curLow = lArr[i];
      const curClose = closes[i];
      const curTime = timestamps[i];
      const curVol = vArr[i];

      switch (state) {
        case 'SearchingForTop': {
          if (i === 0) break; // Require at least 1 preceding bar for lookback

          // Identify candidate swing high higher than previous candle and preceding lookback
          const startLook = Math.max(0, i - lookbackForTop);
          let isLocalPeak = true;
          for (let j = startLook; j < i; j++) {
            if (hArr[j] > curHigh) {
              isLocalPeak = false;
              break;
            }
          }

          if (isLocalPeak && curHigh >= hArr[i - 1]) {
            candidateTop = curHigh;
            candidateTopIndex = i;
            topConfirmBars = 0;
            state = 'ConfirmingTop';
          }
          break;
        }

        case 'ConfirmingTop': {
          if (curHigh > candidateTop) {
            // New higher high replaces candidate top; restart 3-day countdown
            candidateTop = curHigh;
            candidateTopIndex = i;
            topConfirmBars = 0;
          } else if (curHigh === candidateTop) {
            // Touched the exact same high again; reset countdown per Nicolas Darvas rule
            topConfirmBars = 0;
          } else {
            topConfirmBars++;
            if (topConfirmBars >= confirmationBars) {
              // Step 2 complete: Top is confirmed!
              // Look for lowest reaction low formed in the pullback since candidateTopIndex
              const searchStart = Math.min(candidateTopIndex + 1, i);
              let minL = lArr[searchStart];
              let minLIdx = searchStart;
              for (let j = searchStart; j <= i; j++) {
                if (lArr[j] <= minL) { // pick most recent occurrence
                  minL = lArr[j];
                  minLIdx = j;
                }
              }

              candidateBottom = minL;
              candidateBottomIndex = minLIdx;
              // Number of bars strictly after the lowest low touch
              bottomConfirmBars = i - minLIdx;
              state = 'ConfirmingBottom';
            }
          }
          break;
        }

        case 'ConfirmingBottom': {
          // If price touches or breaks above candidateTop, abort bottom search and update top
          if (curHigh >= candidateTop) {
            candidateTop = curHigh;
            candidateTopIndex = i;
            topConfirmBars = 0;
            state = 'ConfirmingTop';
            break;
          }

          if (curLow < candidateBottom) {
            // New lower low found; reset candidate bottom and restart countdown
            candidateBottom = curLow;
            candidateBottomIndex = i;
            bottomConfirmBars = 0;
          } else if (curLow === candidateBottom) {
            // Touched the exact same low again; reset countdown
            bottomConfirmBars = 0;
          } else {
            bottomConfirmBars++;
            if (bottomConfirmBars >= confirmationBars) {
              // Step 4 complete: Bottom is confirmed!
              const boxTop = candidateTop;
              const boxBottom = candidateBottom;
              const heightPct = ((boxTop - boxBottom) / boxBottom) * 100;

              if (heightPct >= minBoxHeightPct && heightPct <= maxBoxHeightPct) {
                // Step 5: Create official Darvas Box
                const startIdx = Math.min(candidateTopIndex, candidateBottomIndex);
                activeBox = {
                  high: boxTop,
                  low: boxBottom,
                  startTimestamp: timestamps[startIdx],
                  endTimestamp: curTime,
                  highIndex: candidateTopIndex,
                  lowIndex: candidateBottomIndex,
                  state: 'confirmed'
                };
                state = 'BoxActive';
              } else {
                // Range too flat or excessively wide, skip and search for new top
                state = 'SearchingForTop';
              }
            }
          }
          break;
        }

        case 'BoxActive': {
          if (!activeBox) {
            state = 'SearchingForTop';
            break;
          }

          activeBox.endTimestamp = curTime;

          const isBullBreak = breakoutOnClose
            ? curClose > activeBox.high
            : (curHigh > activeBox.high || curClose > activeBox.high);

          const isBearBreak = breakoutOnClose
            ? curClose < activeBox.low
            : (curLow < activeBox.low || curClose < activeBox.low);

          // Step 7: Bullish Breakout Check
          if (isBullBreak) {
            activeBox.state = 'bullish_breakout';
            activeBox.breakoutTimestamp = curTime;
            activeBox.breakoutPrice = curClose;
            activeBox.breakoutVolume = curVol;
            detectedBoxes.push({ ...activeBox });
            activeBox = null;

            // Immediately start looking for next Darvas Box from breakout high
            candidateTop = curHigh;
            candidateTopIndex = i;
            topConfirmBars = 0;
            state = 'ConfirmingTop';
            break;
          }

          // Step 8: Bearish Breakdown Check
          if (isBearBreak) {
            activeBox.state = 'bearish_breakdown';
            activeBox.breakoutTimestamp = curTime;
            activeBox.breakoutPrice = curClose;
            activeBox.breakoutVolume = curVol;
            detectedBoxes.push({ ...activeBox });
            activeBox = null;

            state = 'SearchingForTop';
            break;
          }

          // Step 9: Max Duration Guard to prevent multi-month indefinite boxes
          if (i - activeBox.highIndex >= maxBoxBars) {
            activeBox.state = 'confirmed';
            detectedBoxes.push({ ...activeBox });
            activeBox = null;
            state = 'SearchingForTop';
          }
          break;
        }
      }
    }

    // Step 6: If still in BoxActive at the end of the data, record as forming/active box
    if (state === 'BoxActive' && activeBox) {
      activeBox.state = 'forming';
      activeBox.endTimestamp = timestamps[n - 1];
      detectedBoxes.push({ ...activeBox });
    }

    return detectedBoxes;
  }
}
