# Technical Analysis

AGENT.md の要約から分割した、テクニカル指標・シグナル・候補スコア算出の詳細。
株価データの取得元は [`data-sources.md`](./data-sources.md)、損切/利確ルールは [`risk-management.md`](./risk-management.md) を参照。
Non-negotiable rules・優先順位は AGENT.md 側が正であり、本ファイルの内容と矛盾する場合は AGENT.md を優先する。

## Technical analysis
目的は日足によるスイングトレード候補探索。

必須:
- MACD を必須指標の1つとする。
- 単一指標だけで Entry/Exit を判断しない。
- ダマシ対策を重視する。
- Long と Short は別ロジックで評価可能にする。
- 判定理由、判定日、適用戦略を保存/表示可能にする。
- 指標期間・閾値をコード中へ散在させず戦略パラメータへ集約する。
- MACD は遅行性もあるため、反応速度を理由に単独依存しない。

併用候補: EMA/SMA、RSI、ATR、Bollinger Bands、ADX、出来高、高値/安値ブレイク。
全部を使うこと自体を目的にせず、説明可能・検証可能な組み合わせにする。

初期指標セット(仮決定): 少数精鋭で MACD + EMA(トレンド) + 出来高 + ATR の4種とし、パラメータはコード中に散在させず戦略パラメータへ集約する。不足が判明したら後で追加する。

- MACD: 期間 12/26/9(仮値)。
- EMA: 20/50/200(仮値)の3本でトレンド判定(短期・中期・長期の位置関係/クロス)。
- 出来高: 直近20日平均出来高に対する候補日の出来高倍率(例: 1.5倍以上、仮値)。
- ATR: 期間14日(仮値)。損切/利確ラインのボラティリティ適応型算出(ATR倍数)に用いる（詳細は [`risk-management.md`](./risk-management.md)）。

指標計算の入力は、[`data-sources.md`](./data-sources.md) の point-in-time 規則で生成した同一版の調整済み OHLCV とする。provider の `adjclose` を直接入力にせず、分析時点より後に効力発生または利用可能になった企業アクションを適用しない。

初期実装の MACD は、fast/slow と signal のすべてに後述の SMA seed EMA を使用し、`MACD = EMA(fast) - EMA(slow)`、`Histogram = MACD - Signal` とする。アルゴリズム識別子は `macd-ema-sma-seed-v1` とする。

出来高の「直近20日平均」は候補日自身を含めず、候補日の直前20本の有効日足を対象とする。出来高倍率は `候補日の調整済み出来高 / 直前20本平均` とし、分母が0の場合は倍率を欠損状態 `ReferenceAverageZero` として保持して、0倍や無限大を推測しない。アルゴリズム識別子は平均を `volume-trailing-prior-bars-sma-v1`、倍率を `volume-current-to-prior-average-v1` とする。

### EMA calculation contract
EMA は銘柄ごとの上場来履歴を固定起点とし、現在日から遡る移動窓を起点にしない。休場日・売買停止日は補間せず、有効な日足を取引日昇順で使用する。履歴の完全性を確認できない場合は計算しない。

期間を `N`、入力終値を `Close[i]` とし、`alpha = 2 / (N + 1)` とする。先頭 `N` 本の単純平均を `N` 本目のシードとし、以後を次式で計算する。

```text
EMA[N-1] = SMA(Close[0..N-1])
EMA[i]   = alpha * Close[i] + (1 - alpha) * EMA[i-1]  (i >= N)
```

計算は C# `decimal` で中間丸めを行わず、表示時のみ丸める。アルゴリズム識別子 `ema-sma-seed-v1`、計算起点、入力データ版を分析結果とともに保存する。

初期戦略では EMA200 を必須とする。EMA200の当日値だけなら200本、前日値やクロスを使う判定には201本の有効日足を必要とするため、スキャンの最低履歴本数は201本とする。不足銘柄は Long/Short 候補から除外し、`InsufficientHistory`、保有本数、必要本数を保存・表示する。短いEMAへの代替やスコア重みの再配分は行わない。

指標エンジンは、Infrastructureのpoint-in-time選択・企業アクション調整境界だけが生成できる検証済み系列型を入力とし、manifestから再構築した日付ごとの価格revision ID、価格revision集合hash、企業アクション集合hash、manifest hashを保持する。評価日を末尾とする日付昇順・重複なしの確定済みまたは訂正済み日足だけを受け付け、manifestと件数・先頭日・末尾日・最低必要本数が一致しない系列、評価日より後の足、暫定足・無効足は `InvalidData` として計算しない。`HistoryIncomplete`、`PointInTimeUnverified`、企業アクションの `ReconciliationRequired` も成功値へフォールバックしない。

Long/Short 判定条件(仮決定、非対称):
- Long Entry: 当日 MACD line > signal、EMA20 > EMA50 > EMA200、出来高倍率が方向別の最低値以上なら候補とする。出来高倍率は候補の足切り用フィルタとしてのみ使用し、Longのスコア構成要素にはしない。
- Short Entry: 当日 MACD line < signal、EMA20 < EMA50 < EMA200、出来高倍率が方向別の最低値以上の全条件一致を必須とする。信用売りは踏み上げ等のリスクが Long と非対称なため、出来高確認もスコア構成要素として保持する。

初期実装 `candidate-scoring-engine-v1` はMACDの「一致」を当日のline/signal位置関係、EMAトレンドを3本のstrict stackとして判定する。等値は一致に含めない。当日クロスだけには限定せず、前日が不一致なら`Fresh`、前日から一致なら`Continuation`として理由を区別する。出来高倍率の初期最低値はLong/Shortとも1.5（境界を含む）だが方向別パラメータとし、`ReferenceAverageZero`等で倍率を評価できない場合は`InvalidData`としてスコアリングしない。

## Candidate score calculation
候補スコア算出方法(仮決定): 各指標の一致度・強度を重み付けして合計する加算方式。0〜100の数値スコアと、高/中/低の信頼度ラベルの両方を保持・表示する。重み・閾値は戦略パラメータへ外部化し、完全な正規化JSON/hashとして凍結する。必須条件をすべて満たした候補だけをスコアリング対象とする。

`candidate-scoring-engine-v1` の初期仮値と計算契約:

- Long: MACD 50、EMA 50、出来高 0。Short: MACD 40、EMA 40、出来高 20。方向ごとの合計は必ず100とする。
- MACDの方向gapはLong=`line-signal`、Short=`signal-line`。EMAの方向gapは、Longでは`min(EMA20-EMA50, EMA50-EMA200)`、Shortでは`min(EMA50-EMA20, EMA200-EMA50)`。
- 価格水準や分割単位に依存しないよう、MACD/EMA強度は`gap / (gap + ATR14 * atrNormalizationScale)`（初期scale=1）で0〜1へ正規化する。ATR=0かつgap>0は強度1とする。
- 条件一致時の得点係数は`matchedBaseFraction + (1-matchedBaseFraction) * strength`（初期base=0.5）。これにより弱い一致も条件通過として扱いつつ、強度で順位差を付ける。
- Shortの出来高強度は、最低倍率1.5を0、full-strength倍率2.0を1として線形正規化し、同じ得点係数を適用する。Longの出来高componentは監査用にweight/awardedとも0で保持する。
- component得点は中間丸めせず、合計を最後に1回だけ`MidpointRounding.AwayFromZero`で整数化し、0〜100へ制限する。信頼度はHigh >= 80、Medium >= 60、Low < 60とする（境界を含む）。
- score componentのraw JSONには固定schema version、方向、当日/前日の生値、directional gap、ATR、正規化強度、閾値、使用indicatorのinput hashを決定的な順序で保存する。

これらの数値はバックテスト前の仮値であり、変更時はparameter snapshotとcandidate engine versionを更新する。全期間min/maxや当日ユニバース内percentileによる正規化は、未来データ混入・母集団依存を避けるため使用しない。
スコアを勝率・利益保証として表現しない。

履歴不足またはデータ不正で必須指標を計算できない銘柄はスコアリングしない。必須指標を除いて100点へ再配分してはならない。

## All-instrument scan contract

初期ユニバースは設定化した`TSE`・`DomesticCommonStock`・`Listed`・`ScanEligibility.Eligible`の積集合とする。`Unknown`を適格と推測せず、評価日に有効で分析時点に利用可能かつrecorded cutoff以前の銘柄マスタrevisionだけを使用する。Shortのテクニカル候補と実際の売建可否・規制状態は別情報とし、売建可否を候補engineへ混入させない。

Applicationの全銘柄スキャンは、評価時点で有効かつ利用可能だった銘柄コードrevisionをinstrument masterへ結び付け、銘柄コード昇順の決定的な順序で各銘柄の検証済みpoint-in-time requestから指標を1回計算し、その同一結果をLong/Shortへ各1回評価する。1銘柄の予期しない失敗で後続を停止せず、進捗・候補件数・失敗件数と`Succeeded`/`PartiallySucceeded`/`Failed`の集計を返す。indicator resultはrun/manifest/instrument/evaluation date/manifest hashのidentityを保持し、入力bundleとの不一致をfail-closedとする。runとengine version・parameter snapshotの不一致も同様に拒否する。parameter snapshotの正規化JSONとhashにはstrategy key/version、candidate algorithm version、型付きパラメータ本体を含める。候補順位は方向別にscore降順、同点は銘柄コード昇順とする。

## Evaluation time
`EvaluationBarDate` は分析に使った最新の確定済み日足、`AnalyzedAt` は実際に分析を実行したJST日時とし、分離して保存する。分析には `EvaluationBarDate` 以前かつ `AnalyzedAt` 時点で利用可能なデータだけを使う。15:30経過だけで日足確定とみなさない。当日の候補は原則として次の取引セッションに向けた判断支援情報である。

## Look-ahead bias
未来データ混入は重大な不具合として扱う。
過去日のシグナル計算で、その日より後の情報を使わない。

禁止例:
- 全期間正規化による未来値混入
- centered moving average 等の未来値参照
- 翌日価格で当日サインを確定
- 後日判明した情報を過去時点の AI 判定へ混入
- 後日発表・訂正された企業アクションを、当時利用可能だったものとして適用

将来バックテストを追加する場合も同じ。

企業アクションは `effective_date <= EvaluationBarDate` と `available_at <= AnalyzedAt` の両方を満たす版だけを適用する。`available_at` を復元できない履歴データは point-in-time 保証なしとして通常運用・正式なバックテスト結果と区別する。

## Reproducibility
分析結果には少なくとも、`EvaluationBarDate`、`AnalyzedAt`、使用した日足revision集合または入力manifest/hash、企業アクションIDとrevision、調整係数、指標エンジン版、戦略パラメータの完全スナップショットとhash、算出指標値、判定理由を凍結保存する。後日の価格訂正や企業アクション訂正で過去の分析結果を上書きせず、新しい分析runとして保存する。

## Reference implementation
テクニカル分析では、利用可能なら以下を参考にする。

`C:\Users\su\source\repos\stock-simulator-codex`

- まず既存の指標、戦略、パラメータ、判定方法を確認する。
- 本アプリの目的に合う部分だけ参考にする。
- 仮想売買、自動約定、自動履歴生成の思想は持ち込まない。
- 必要がない限り参照先を編集しない。
- 参照先が存在しない環境でも本リポジトリの作業を停止しない。
