import { describe, it, expect, beforeEach } from 'vitest';
import { DarvasBoxDetectorService, DarvasBox } from './darvas-box-detector.service';

describe('DarvasBoxDetectorService', () => {
  let detector: DarvasBoxDetectorService;

  beforeEach(() => {
    detector = new DarvasBoxDetectorService();
  });

  it('should be created', () => {
    expect(detector).toBeTruthy();
  });

  it('should return an empty array if data has fewer than 8 candles', () => {
    const dates = ['2026-01-01', '2026-01-02', '2026-01-03'];
    const closes = [100, 102, 101];
    const boxes = detector.detectBoxes(dates, closes, closes, closes, closes);
    expect(boxes).toEqual([]);
  });

  // =========================================================================
  // TEST 1: Bullish Breakout Box (Full Lifecycle)
  // Top Confirmation -> Bottom Confirmation -> Active Box -> Bullish Breakout
  // =========================================================================
  it('Box 1: should correctly follow Top confirmation -> Bottom confirmation -> Active Box -> Bullish Breakout', () => {
    const dates: string[] = [];
    const opens: number[] = [];
    const highs: number[] = [];
    const lows: number[] = [];
    const closes: number[] = [];
    const volumes: number[] = [];

    function addBar(d: string, o: number, h: number, l: number, c: number, v = 100000) {
      dates.push(d);
      opens.push(o);
      highs.push(h);
      lows.push(l);
      closes.push(c);
      volumes.push(v);
    }

    // Days 0-3: Uptrend runup
    addBar('2026-01-01', 100, 102, 99, 101);
    addBar('2026-01-02', 101, 105, 100, 104);
    addBar('2026-01-03', 104, 108, 103, 107);
    addBar('2026-01-04', 107, 110, 106, 109);

    // Day 4: Candidate Peak High = 120 (Index 4)
    addBar('2026-01-05', 109, 120, 108, 118);

    // Days 5-7: 3-day Top confirmation (all highs <= 120)
    addBar('2026-01-06', 117, 119, 114, 115); // confirm 1
    addBar('2026-01-07', 114, 118, 111, 112); // confirm 2
    addBar('2026-01-08', 112, 117, 106, 110); // confirm 3 -> Top 120 confirmed at Day 7! Lowest low = 106 (Index 7)

    // Days 8-10: 3-day Bottom confirmation (all lows >= 106 and highs <= 120)
    addBar('2026-01-09', 110, 116, 107, 112); // confirm 1
    addBar('2026-01-10', 112, 118, 108, 115); // confirm 2
    addBar('2026-01-11', 115, 117, 109, 114); // confirm 3 -> Bottom 106 confirmed! Box Active!

    // Day 11: Inside box
    addBar('2026-01-12', 114, 119, 111, 116);

    // Day 12: Bullish Breakout (High = 125 > 120, Close = 124 > 120)
    addBar('2026-01-13', 116, 125, 115, 124, 250000);

    const boxes = detector.detectBoxes(dates, opens, highs, lows, closes, volumes);

    expect(boxes.length).toBeGreaterThanOrEqual(1);
    const box = boxes[0];

    // Verify calculated high, low, state and breakout values
    expect(box.high).toBe(120);
    expect(box.low).toBe(106);
    expect(box.highIndex).toBe(4);
    expect(box.lowIndex).toBe(7);
    expect(box.state).toBe('bullish_breakout');
    expect(box.breakoutPrice).toBe(124);
    expect(box.breakoutVolume).toBe(250000);
    expect(box.startTimestamp).toBe(new Date('2026-01-05').getTime());
    expect(box.endTimestamp).toBe(new Date('2026-01-13').getTime());
    expect(box.breakoutTimestamp).toBe(new Date('2026-01-13').getTime());
  });

  // =========================================================================
  // TEST 2: Bearish Breakdown Box
  // Top Confirmation -> Bottom Confirmation -> Active Box -> Bearish Breakdown
  // =========================================================================
  it('Box 2: should correctly follow Top confirmation -> Bottom confirmation -> Active Box -> Bearish Breakdown', () => {
    const dates: string[] = [];
    const opens: number[] = [];
    const highs: number[] = [];
    const lows: number[] = [];
    const closes: number[] = [];

    function addBar(d: string, o: number, h: number, l: number, c: number) {
      dates.push(d);
      opens.push(o);
      highs.push(h);
      lows.push(l);
      closes.push(c);
    }

    // Baseline lookback
    addBar('2026-02-01', 200, 205, 198, 202);
    addBar('2026-02-02', 202, 210, 200, 208);
    addBar('2026-02-03', 208, 215, 206, 212);
    addBar('2026-02-04', 212, 220, 210, 218);

    // Day 4: Candidate Peak High = 230
    addBar('2026-02-05', 218, 230, 215, 225);

    // Days 5-7: 3-day Top confirmation (H <= 230)
    addBar('2026-02-06', 224, 228, 218, 220);
    addBar('2026-02-07', 220, 226, 214, 216);
    addBar('2026-02-08', 215, 222, 205, 208); // Bottom candidate = 205

    // Days 8-10: 3-day Bottom confirmation (L >= 205, H <= 230)
    addBar('2026-02-09', 208, 218, 206, 212);
    addBar('2026-02-10', 212, 220, 207, 215);
    addBar('2026-02-11', 214, 219, 208, 211); // Bottom 205 confirmed! Box Active!

    // Day 11: Inside box
    addBar('2026-02-12', 211, 217, 206, 209);

    // Day 12: Bearish Breakdown (Low = 198 < 205, Close = 200 < 205)
    addBar('2026-02-13', 208, 210, 198, 200);

    const boxes = detector.detectBoxes(dates, opens, highs, lows, closes);

    expect(boxes.length).toBeGreaterThanOrEqual(1);
    const box = boxes[0];

    expect(box.high).toBe(230);
    expect(box.low).toBe(205);
    expect(box.state).toBe('bearish_breakdown');
    expect(box.breakoutPrice).toBe(200);
    expect(box.startTimestamp).toBe(new Date('2026-02-05').getTime());
    expect(box.endTimestamp).toBe(new Date('2026-02-13').getTime());
    expect(box.breakoutTimestamp).toBe(new Date('2026-02-13').getTime());
  });

  // =========================================================================
  // TEST 3: Active Ongoing Box (No Breakout Yet)
  // Top Confirmation -> Bottom Confirmation -> Stays in BoxActive up to current bar
  // =========================================================================
  it('Box 3: should correctly detect an active/forming box that remains unbroken at end of dataset', () => {
    const dates: string[] = [];
    const opens: number[] = [];
    const highs: number[] = [];
    const lows: number[] = [];
    const closes: number[] = [];

    function addBar(d: string, o: number, h: number, l: number, c: number) {
      dates.push(d);
      opens.push(o);
      highs.push(h);
      lows.push(l);
      closes.push(c);
    }

    addBar('2026-03-01', 50, 52, 49, 51);
    addBar('2026-03-02', 51, 54, 50, 53);
    addBar('2026-03-03', 53, 56, 52, 55);
    addBar('2026-03-04', 55, 58, 54, 57);

    // Day 4: Candidate Peak High = 65
    addBar('2026-03-05', 57, 65, 56, 62);

    // Days 5-7: 3-day Top confirmation
    addBar('2026-03-06', 62, 64, 58, 60);
    addBar('2026-03-07', 60, 63, 57, 59);
    addBar('2026-03-08', 59, 62, 54, 56); // Candidate Bottom = 54

    // Days 8-10: 3-day Bottom confirmation
    addBar('2026-03-09', 56, 61, 55, 58);
    addBar('2026-03-10', 58, 62, 56, 60);
    addBar('2026-03-11', 60, 63, 57, 61); // Bottom 54 confirmed!

    // Days 11-13: Fluctuates inside [54, 65]
    addBar('2026-03-12', 61, 64, 58, 62);
    addBar('2026-03-13', 62, 63, 57, 59);
    addBar('2026-03-14', 59, 62, 56, 60);

    const boxes = detector.detectBoxes(dates, opens, highs, lows, closes);

    expect(boxes.length).toBe(1);
    const box = boxes[0];

    expect(box.high).toBe(65);
    expect(box.low).toBe(54);
    expect(box.state).toBe('forming');
    expect(box.breakoutTimestamp).toBeUndefined();
    expect(box.endTimestamp).toBe(new Date('2026-03-14').getTime());
  });

  // =========================================================================
  // TEST 4: Multiple Sequential Boxes (Stacking on Breakout)
  // Box A (Breakout) -> Immediately starts Box B from Breakout High -> Box B (Breakout)
  // =========================================================================
  it('Box 4 & 5: should detect multiple independent sequential stacked boxes across timeline', () => {
    const dates: string[] = [];
    const opens: number[] = [];
    const highs: number[] = [];
    const lows: number[] = [];
    const closes: number[] = [];

    function addBar(d: string, o: number, h: number, l: number, c: number) {
      dates.push(d);
      opens.push(o);
      highs.push(h);
      lows.push(l);
      closes.push(c);
    }

    // --- BOX 1 SEQUENCE ---
    addBar('2026-04-01', 100, 102, 99, 101);
    addBar('2026-04-02', 101, 104, 100, 103);
    addBar('2026-04-03', 103, 106, 102, 105);
    addBar('2026-04-04', 105, 108, 104, 107);
    addBar('2026-04-05', 107, 115, 106, 114); // Peak 1 = 115 (Index 4)

    addBar('2026-04-06', 113, 114, 110, 111);
    addBar('2026-04-07', 111, 113, 108, 109);
    addBar('2026-04-08', 109, 112, 104, 108); // Bottom 1 = 104 (Index 7)

    addBar('2026-04-09', 108, 111, 106, 109);
    addBar('2026-04-10', 109, 113, 107, 110);
    addBar('2026-04-11', 110, 114, 108, 112); // Box 1 Confirmed [104 - 115]

    // Day 11: Breakout of Box 1 (High = 125, Close = 124). Becomes candidate top for Box 2!
    addBar('2026-04-12', 118, 125, 116, 124);

    // --- BOX 2 SEQUENCE ---
    // 3 days confirming Box 2 top at 125
    addBar('2026-04-13', 123, 124, 119, 121);
    addBar('2026-04-14', 121, 123, 118, 119);
    addBar('2026-04-15', 119, 122, 114, 116); // Bottom 2 = 114

    // 3 days confirming Box 2 bottom at 114
    addBar('2026-04-16', 116, 121, 115, 118);
    addBar('2026-04-17', 118, 123, 116, 120);
    addBar('2026-04-18', 120, 124, 117, 122); // Box 2 Confirmed [114 - 125]

    // Breakout of Box 2 (High = 132, Close = 130)
    addBar('2026-04-19', 122, 132, 121, 130);

    const boxes = detector.detectBoxes(dates, opens, highs, lows, closes);

    expect(boxes.length).toBe(2);

    // Box 1 assertions
    expect(boxes[0].high).toBe(115);
    expect(boxes[0].low).toBe(104);
    expect(boxes[0].state).toBe('bullish_breakout');
    expect(boxes[0].breakoutPrice).toBe(124);

    // Box 2 assertions
    expect(boxes[1].high).toBe(125);
    expect(boxes[1].low).toBe(114);
    expect(boxes[1].state).toBe('bullish_breakout');
    expect(boxes[1].breakoutPrice).toBe(130);
  });

  // =========================================================================
  // TEST 5: Top Invalidation (Breached Before Bottom Confirmed)
  // Verifies that a box attempt is aborted if price breaches candidateTop early
  // =========================================================================
  it('should invalidate candidate bottom and restart top confirmation if candidateTop is breached early', () => {
    const dates: string[] = [];
    const opens: number[] = [];
    const highs: number[] = [];
    const lows: number[] = [];
    const closes: number[] = [];

    function addBar(d: string, o: number, h: number, l: number, c: number) {
      dates.push(d);
      opens.push(o);
      highs.push(h);
      lows.push(l);
      closes.push(c);
    }

    addBar('2026-05-01', 100, 102, 99, 101);
    addBar('2026-05-02', 101, 104, 100, 103);
    addBar('2026-05-03', 103, 106, 102, 105);
    addBar('2026-05-04', 105, 108, 104, 107);
    addBar('2026-05-05', 107, 115, 106, 114); // Peak 115

    addBar('2026-05-06', 113, 114, 110, 111);
    addBar('2026-05-07', 111, 113, 108, 109);
    addBar('2026-05-08', 109, 112, 104, 108); // Top 115 confirmed, bottom candidate 104

    addBar('2026-05-09', 108, 110, 105, 109); // Bottom confirm 1
    // Day 9: Surprise surge breaches 115 before 3 bottom confirmation days!
    addBar('2026-05-10', 110, 118, 109, 117); // Invalidated! New top candidate = 118

    // 3 days confirming new top 118
    addBar('2026-05-11', 116, 117, 112, 114);
    addBar('2026-05-12', 114, 116, 110, 112);
    addBar('2026-05-13', 112, 115, 108, 110); // New top 118 confirmed! Bottom candidate = 108

    // 3 days confirming bottom 108
    addBar('2026-05-14', 110, 114, 109, 112);
    addBar('2026-05-15', 112, 115, 110, 113);
    addBar('2026-05-16', 113, 116, 111, 114); // Bottom 108 confirmed! Active box [108, 118]

    addBar('2026-05-17', 114, 122, 113, 121); // Breakout!

    const boxes = detector.detectBoxes(dates, opens, highs, lows, closes);

    // Only 1 valid box should be created (with high=118, low=108), the premature attempt at 115 was cleanly aborted
    expect(boxes.length).toBe(1);
    expect(boxes[0].high).toBe(118);
    expect(boxes[0].low).toBe(108);
    expect(boxes[0].state).toBe('bullish_breakout');
    expect(boxes[0].breakoutPrice).toBe(121);
  });

  // =========================================================================
  // TEST 6: Equal High (Double Top) resets confirmation countdown
  // Verifies Nicolas Darvas rule: touching peak again resets 3-day countdown
  // =========================================================================
  it('should reset top confirmation countdown when price touches exact candidateTop (equal high)', () => {
    const dates: string[] = [];
    const opens: number[] = [];
    const highs: number[] = [];
    const lows: number[] = [];
    const closes: number[] = [];

    function addBar(d: string, o: number, h: number, l: number, c: number) {
      dates.push(d);
      opens.push(o);
      highs.push(h);
      lows.push(l);
      closes.push(c);
    }

    addBar('2026-06-01', 100, 102, 99, 101);
    addBar('2026-06-02', 101, 105, 100, 104);
    addBar('2026-06-03', 104, 108, 103, 107);
    addBar('2026-06-04', 107, 110, 106, 109);
    addBar('2026-06-05', 109, 120, 108, 118); // Candidate peak 120 (Index 4)

    addBar('2026-06-06', 117, 118, 114, 116); // Day 1 confirm
    // Day 2 touches exact 120 again! Count must reset to 0
    addBar('2026-06-07', 116, 120, 115, 117); // Equal high touches peak!
    addBar('2026-06-08', 117, 118, 112, 114); // Now Day 1 confirm again
    addBar('2026-06-09', 114, 117, 110, 112); // Day 2 confirm
    addBar('2026-06-10', 112, 116, 106, 110); // Day 3 confirm -> Top confirmed at Day 9, lowest low = 106

    // 3 days confirming bottom 106
    addBar('2026-06-11', 110, 115, 107, 112);
    addBar('2026-06-12', 112, 116, 108, 114);
    addBar('2026-06-13', 114, 117, 109, 115); // Box [106, 120] confirmed

    const boxes = detector.detectBoxes(dates, opens, highs, lows, closes);
    expect(boxes.length).toBe(1);
    expect(boxes[0].high).toBe(120);
    expect(boxes[0].low).toBe(106);
    expect(boxes[0].state).toBe('forming');
  });

  // =========================================================================
  // TEST 7: False Breakout Prevention (Wick vs Close)
  // Verifies that intraday wick above box does not trigger breakout if close is inside
  // =========================================================================
  it('should ignore intraday wick spikes above box when breakoutOnClose is true', () => {
    const dates: string[] = [];
    const opens: number[] = [];
    const highs: number[] = [];
    const lows: number[] = [];
    const closes: number[] = [];

    function addBar(d: string, o: number, h: number, l: number, c: number) {
      dates.push(d);
      opens.push(o);
      highs.push(h);
      lows.push(l);
      closes.push(c);
    }

    addBar('2026-07-01', 100, 102, 99, 101);
    addBar('2026-07-02', 101, 105, 100, 104);
    addBar('2026-07-03', 104, 108, 103, 107);
    addBar('2026-07-04', 107, 110, 106, 109);
    addBar('2026-07-05', 109, 120, 108, 118); // Peak 120

    addBar('2026-07-06', 117, 119, 114, 115);
    addBar('2026-07-07', 114, 118, 111, 112);
    addBar('2026-07-08', 112, 117, 106, 110); // Top confirmed at 120, bottom candidate 106

    addBar('2026-07-09', 110, 116, 107, 112);
    addBar('2026-07-10', 112, 118, 108, 115);
    addBar('2026-07-11', 115, 117, 109, 114); // Box active [106, 120]

    // Day 11: Wick spike to 123 (bull trap), but candle closes at 117 (inside the box)
    addBar('2026-07-12', 116, 123, 114, 117);

    const boxes = detector.detectBoxes(dates, opens, highs, lows, closes, undefined, { breakoutOnClose: true });
    expect(boxes.length).toBe(1);
    // Box should still be active/forming, NOT triggered as breakout
    expect(boxes[0].state).toBe('forming');
    expect(boxes[0].breakoutTimestamp).toBeUndefined();
  });
});

