# KeyGrid Specification v1.13

**Version:** 1.13
**対象OS:** Windows 11（x64）
**アプリケーション名:** KeyGrid
**実装言語:** C#
**UIフレームワーク:** WPF（オーバーレイの描画のみ。位置・サイズはWin32で指定）
**ランタイム:** .NET 10（LTS）
**開発環境:** Visual Studio Code

---

## 0. 用語定義

| 用語 | 定義 |
|---|---|
| 仮想画面 | 全モニターの表示領域を包含する外接矩形（物理ピクセル） |
| モニター表示領域 | 各モニターの表示領域（物理ピクセル） |
| 選択領域 | 現在の選択範囲。モニター表示領域との交差部分の集合で、非矩形になりうる |
| 外接矩形 | 選択領域を包含する最小の矩形。分割・基準点の算出に用いる |
| 段階 | 1回の絞り込み。第1段階は列キーと行キーでセルを選び、第2段階は矢印キーでカーソルを移動する |
| 列数・行数 | 第1段階では4.1で算出し、第2段階では固定の5×5を使う |
| セル | 外接矩形を列数×行数に分割した1つの矩形 |
| 有効セル | セルと選択領域の交差が存在するセル |
| 打鍵履歴 | 第1段階で押したキーの列。Backspaceで末尾から1つずつ取り消す。選択領域は履歴から再計算する |
| プライマリモニター | Windowsが主ディスプレイとして指定したモニター（MONITORINFOF_PRIMARY） |
| 実効DPI | モニターごとの現在のDPI設定。100%を1.0とする倍率で扱う |
| DIP | 96 DPIを1.0とするdevice-independent pixel。WPF描画の座標単位 |
| 有効セル | セルと選択領域の交差が存在するセル |
| 選択カーソル | 第2段階で選択中の有効セル |
| 基準点 | 外接矩形の中心（4.4の規則で有効座標に補正する） |
| 確定座標 | 最終的にクリックする座標 |
| 確定キー | `Enter`（左クリック）、`Shift+Enter`（右クリック）、`Ctrl+Enter`（中クリック）の総称 |
| 連続モード | 確定後も選択を終了せず、第1段階から選択をやり直すモード。グリッド表示中にTabで切り替える |
| Range selection | A two-point selection started with `R` in Stage 2. The first point becomes the anchor and the current Stage 2 target becomes the endpoint. `Enter` performs a left click at the anchor and a Shift+left click at the endpoint |
| Range anchor | The physical-pixel screen coordinate captured when range selection starts. It remains fixed while the Stage 2 cursor or Stage 1 region changes |
| Scroll Mode | A Stage 2 sub-mode entered with `PageUp` or `PageDown`. Arrow and Page keys send mouse wheel input to the current target; `Esc` returns to Stage 2 |
| Scroll target | The current Stage 2 target coordinate, recalculated from the selection when each scroll input is dispatched |
| 主オーバーレイ | キーボード入力を受け取るオーバーレイウィンドウ（1つ） |
| 表示専用オーバーレイ | 主オーバーレイ以外。マウス入力を遮断し、描画のみ行う |

---

## 1. 目的

KeyGrid is a resident utility for left-, right-, and middle-clicking accessible screen locations using only the keyboard.

It displays column and row labels across the virtual screen. The user narrows the target area with one column key and one row key, then moves a cursor through a 5×5 grid with the arrow keys. `Enter` performs a left click; `Shift+Enter` performs a right click; `Ctrl+Enter` performs a middle (wheel) click. In Stage 2, `R` starts a two-point range selection so the user can extend a browser text selection from an anchor to a second point.

### 基本方針

- マウスを使わずに、通常の画面操作で届く位置をクリックできること。
- キーボード操作だけで完結すること。
- 操作をできる限り単純にすること。
- 常駐時もCPU・メモリ消費を抑えること。
- 初期バージョンでは必要最小限の機能に限定すること。

## 2. 動作環境・対象範囲

### 2.1 動作環境

- Windows 11（x64）
- 通常ユーザー権限で動作
- マルチモニター対応
- モニターごとに異なる解像度・DPI（拡大率100・125・150・175・200%）に対応

### 2.2 対象領域

- アプリケーションのクライアント領域
- タイトルバー・ウィンドウ枠
- デスクトップ
- タスクバー（Phase 1で、通常時と自動非表示時の両方でオーバーレイが覆えることを確認する。確認できない場合は初期バージョンの対象外とする）

### 2.3 対象外

- UACのセキュアデスクトップ
- Windowsのロック画面
- 通常権限から操作できない高い整合性レベルのアプリケーション（UIPIにより入力が届かない）
- 上記のほか、SendInputがWindowsのセキュリティ機構により遮断される領域

---

## 3. 基本操作

### 3.1 起動

設定されたホットキーを押すと選択モードを開始し、仮想画面の寸法から第1段階の列数・行数を算出する（4.1）。

初期ホットキー：`Ctrl + Shift + Space`

### 3.2 選択操作

1. ホットキーで全モニターにオーバーレイを表示する。
2. 第1段階の有効セルに列キー＋行キーの2文字ラベルを表示する。
3. 列キー、行キーの順で入力し、第1段階の領域を選択する。
4. 第2段階では選択領域を5×5に分割し、有効セルのうち中央に最も近いセルを初期カーソル位置として赤く表示する。
5. Move the cursor with the arrow keys. Use `Enter` to left-click, `Shift+Enter` to right-click, or `Ctrl+Enter` to middle-click the resolved coordinate. Invalid cells, including gaps between monitors, are skipped.
6. In Stage 2, press `R` without modifiers to set the range anchor at the current target. Move the cursor to the endpoint and press `Enter` to perform a normal left click at the anchor followed by a Shift+left click at the endpoint. `Shift+Enter` and `Ctrl+Enter` are ignored while a range anchor is active.
7. In Stage 2, press `PageUp` or `PageDown` without modifiers to enter Scroll Mode. `Up`/`Down` scroll vertically by one wheel notch, `Left`/`Right` scroll horizontally, and `PageUp`/`PageDown` scroll vertically by five notches. The Stage 2 cursor does not move. `Esc` exits Scroll Mode and returns to Stage 2.
8. 通常モードではオーバーレイを終了し、通常状態に戻る。連続モードではクリック送信後にオーバーレイを再表示し、第1段階（打鍵履歴0個）から選択をやり直す。

Immediately after Stage 1, any confirmation key uses the initially selected valid Stage 2 cell nearest the center.

The range anchor is captured at the exact resolved target coordinate and does not move when the Stage 2 cursor moves. It survives `Backspace` into Stage 1 so the user can change the endpoint region; the anchor remains visible until `Enter` executes the range or `Esc` clears it.

Scroll Mode uses the same current Stage 2 target that a click would use. Range selection and Scroll Mode are mutually exclusive: Page keys are ignored while an anchor is active, and `R` is ignored while Scroll Mode is active.

### 3.3 選択キー

列キーと行キーは、物理キーボード配列に基づく次の2ブロックから割り当てる。同じキーを列と行で共用しない。

```text
左ブロック（列）: Q W E R T A S D F G Z X C V B   (15キー)
右ブロック（行）: Y U I O P H J K L N M             (11キー)
```

- 列数Cが15以下の場合、列には左ブロックの先頭C個を使う。
- 列数Cが15を超える場合、列には左ブロックの15個すべてを使い、続けて右ブロックの先頭C−15個を使う。
- 列が右ブロックを借用していない場合、行数Rが11以下なら行には右ブロックの先頭R個を使う。Rが11を超える場合は右ブロックの11個すべてと、列が使っていない左ブロックのキーを左ブロック順に使う。
- 列が右ブロックを借用している場合、行には右ブロックの残りを元の順で使う。
- C + R ≤ 26を満たすため、必要なキーは必ず確保できる。列と行のキー集合は重複しない。
- Key allocation is used only in Stage 1. Stage 2 uses arrow keys, `Enter`, `Shift+Enter`, and `Ctrl+Enter`.
- 例：1920×1080では第1段階は16×10。列・行キーは上記のブロックから重複なく割り当てる。
- キーの並びは画面上の位置と対応しない。その代わり、列ラベルと行ラベルを画面に表示して、押すキーを示す。
- ブロックはQWERTY/JIS配列の物理的な段を前提とする。AZERTYなど文字配置が異なる配列では、ラベルの文字と物理キー位置が一致しない場合がある。
- Unassigned keys are ignored in Stage 1. In Stage 2, only the arrow keys and the supported confirmation keys are used; other keys are ignored.

### 3.4 確定と終了条件

- In Stage 2, arrow keys move the cursor. `Enter` confirms the cell with a left click; `Shift+Enter` confirms it with a right click; `Ctrl+Enter` confirms it with a middle (wheel) click.
- After Stage 1 is complete, any confirmation key can click the selection region's reference point.
- Before Stage 1 is complete (keystroke history length 0 or 1), all confirmation keys are ignored.
- `Enter` is accepted only with no modifiers. `Shift+Enter` is accepted with Shift alone; `Ctrl+Enter` is accepted with Ctrl alone. Combinations with other modifiers, including Ctrl+Shift, Alt, or the Windows key, are ignored.
- The click kind is captured when `Enter` is pressed. Releasing the modifier before `Enter` does not change the selected click kind; this lets the user release the modifier before the click is dispatched.
- 段階数は2回で固定する。第2段階のカーソル移動回数に上限は設けない。
- 連続モードでは、確定キーによるクリック後も選択を終了せず、第1段階から再開する。1回の選択は通常モードと同じ2段階で、連続モードはその繰り返しである。
- 連続モードはグリッド表示中にTabでON/OFFする。初期状態はOFFとし、ホットキーによる新規開始時はOFFに戻る。
- 連続モードでもEscは選択を破棄して終了する。
- 連続モードでクリックを送信できなかった場合も第1段階から再開し、エラーを通知する。
- Range selection uses plain `Enter` only. The start and end points must resolve to the same top-level window after the overlays are hidden; otherwise no synthetic input is sent, the range is cleared, Stage 2 is restored, and an error is reported.
- A completed range click follows the same normal/continuous-mode completion rules as a normal click. The range anchor is cleared before the next Stage 1 session starts.
- `R` in Stage 1 keeps its normal column/row key behavior. It starts a range only in Stage 2 when no anchor is already active.
- After range selection starts, pressing `Esc` clears the anchor and keeps the selection session open. A second `Esc` exits selection mode.
- Pressing `R` again while an anchor is active is ignored; the anchor changes only after `Esc` clears it.
- Scroll Mode delivers wheel input to the current Stage 2 target. The overlays are hidden only while the synthetic input is sent, then the same Stage 2 selection is restored.
- Scroll Mode uses one notch for arrow keys and five notches for Page keys. Positive vertical notches scroll up and positive horizontal notches scroll right; the key mapping follows the arrow direction.
- `Esc` exits Scroll Mode and returns to Stage 2. A second `Esc` exits selection mode. Scroll Mode does not complete the click or change continuous mode.
- Scroll Mode is ignored while a range anchor is active. `R`, `Tab`, `Backspace`, and confirmation keys are ignored while Scroll Mode is active.
- If the scroll target cannot be resolved or activated, no wheel input is sent; Scroll Mode is cancelled, Stage 2 is restored, and an error is reported.

### 3.5 キー操作

| Key | Action |
|---|---|
| Column / row keys | Select a Stage 1 column and row in order |
| Arrow keys | Move the Stage 2 cursor between valid cells; in Scroll Mode, scroll the target vertically or horizontally by one wheel notch |
| `Enter` | Confirm with a left click; accepted without modifiers |
| `Shift+Enter` | Confirm with a right click; accepted with Shift alone |
| `Ctrl+Enter` | Confirm with a middle (wheel) click; accepted with Ctrl alone |
| `Backspace` | Undo the last Stage 1 key. Arrow movement is not part of the undo history; an active range anchor is preserved. Ignored in Scroll Mode |
| `Tab` | Toggle continuous mode while the grid is visible. Only an unmodified Tab press is accepted; ignored in Scroll Mode |
| `R` (Stage 2) | Set the range anchor at the current target. Only an unmodified press is accepted; ignored while an anchor is already active or Scroll Mode is active |
| `Enter` while a range anchor is active | Execute the range: left click at the anchor, then Shift+left click at the current target |
| `PageUp` / `PageDown` (Stage 2) | Enter Scroll Mode and scroll vertically by five wheel notches. In Scroll Mode, repeat the page scroll |
| `Esc` | Exit Scroll Mode first; then clear an active range anchor; when neither sub-mode is active, discard the selection and exit, including continuous mode |

Backspace undoes one Stage 1 keystroke. In Stage 2 it removes the Stage 1 row key and returns to Stage 1. Arrow movement is not recorded in the history. Range anchors are independent of the history and remain at their captured screen coordinate. Scroll Mode never moves the Stage 2 cursor.

---

## 4. グリッド・領域選択仕様

### 4.1 分割数の算出

第2段階のグリッドは5×5に固定する。仮想画面の幅W・高さH（物理ピクセル）について、第1段階の列数C・行数Rは、C + R ≤ 26の範囲で第2段階後の最大セル寸法を最小化する組とする。

```text
最終幅 = ceil(ceil(W / C) / 5)
最終高さ = ceil(ceil(H / R) / 5)
最小化する値 = max(最終幅, 最終高さ)
```

同じ最大寸法になる場合は、C × Rが大きい組、C + Rが小さい組、Cが小さい組の順に選ぶ。DPIは分割数の計算に使わない。幅や高さが非常に大きくても、合計キー数の上限を超えない。

代表例（サイズは物理ピクセル）：

| 仮想画面 | 第1段階列×行 | ラベル数 | 第2段階後の最大セル寸法 |
|---|---:|---:|---:|
| 1920×1080 | 16×10 | 160 | 24×22 |
| 3840×2160 | 16×10 | 160 | 48×44 |
| 7680×2160 | 20×6 | 120 | 77×72 |
| 7680×4320 | 16×10 | 160 | 96×87 |

### 4.2 分割規則

- 第1段階の分割対象は仮想画面とする。
- 第2段階の分割対象は、第1段階で選択したセルと選択領域との交差の外接矩形とし、固定の5列×5行に分割する。
- 第1段階は4.1で算出した列数・行数で分割し、第2段階は固定の5×5で分割する。
- 整数の境界は、外接矩形の左端（上端）を原点とし、長さLを分割数Nで分けて求める（整数除算）。
  - 境界(k) = 原点 + (k × L + N − 1) ÷ N（k = 1, …, N−1）
  - 境界(0) = 原点、境界(N) = 原点 + L
- 各セルの幅（高さ）は⌊L ÷ N⌋または⌈L ÷ N⌉のいずれかになる。隙間と重なりは生じない。
- 幅または高さが0のセルは無効セルとする。
- 新しい選択領域は、各セルと直前の選択領域との交差とする。交差が空のセルは無効セルとする。
- 第2段階後の幅は⌈⌈W ÷ C⌉ ÷ 5⌉、高さは⌈⌈H ÷ R⌉ ÷ 5⌉以下になる。
- 例：幅1000px・分割数7の場合、境界は原点＋143、＋286、＋429、＋572、＋715、＋858となり、143pxが6セル、142pxが1セルに分かれる。

### 4.3 多重モニター

- 全モニターの表示領域を取得し、それらを包含する外接矩形（仮想画面）を求める。
- 第1段階の対象となる選択領域（段階0）は、全モニター表示領域の集合とする。
- 以降の分割は4.2の規則で行う。
- 選択領域は、モニター表示領域との交差部分の集合として管理する。モニター間の空白領域は選択領域に含めない。
- これにより、モニターが不規則に配置されていても、実際に表示されている領域だけを選択できる。

### 4.4 クリック座標の決定

- 基準点は外接矩形の中心とする。外接矩形を[左, 右) × [上, 下)、幅をWとすると、中心のピクセル座標は「左 + (W − 1) ÷ 2」（整数除算）である。縦方向も同様に求める。
- 基準点が選択領域内にある場合は、その座標を確定座標とする。
- 基準点が選択領域の外（モニター間の空白）にある場合は、選択領域内の整数座標のうち、基準点とのユークリッド距離が最小のものを確定座標とする。
- 距離が同じ候補が複数ある場合は、y座標が小さいものを優先し、同じであればx座標が小さいものを優先する。
- 確定座標は必ずいずれかのモニター表示領域内の整数座標とする。
- 実装では、各モニター表示領域の矩形について基準点の最近点（各座標を矩形の範囲に収めた点）を求め、その中から上記の規則で選ぶ。

### 4.5 座標系

- グリッド計算・クリック座標の管理には物理ピクセル座標を使用する。
- app.manifestでPer-Monitor DPI Awareness V2を指定する。
- オーバーレイの位置・サイズはWin32（SetWindowPos）で物理ピクセル指定する。
- WPFのDIPは、オーバーレイ内の描画にのみ使用する。
- GetDpiForMonitorが失敗した場合は96 DPI（倍率1.0）へフォールバックし、警告をログに記録する。推測値は使わない。
- WPF描画のDIP変換には、各オーバーレイが属するモニターの実効DPIを使う。
- SendInputの仮想画面座標の正規化はCoordinateConverterに集約する。

---

## 5. オーバーレイ表示仕様

### 5.1 表示方式

- 各モニターに1つのオーバーレイウィンドウを表示する。
- 画面全体を覆い、最前面に表示する。背景は半透明とする。
- キーボード入力を受け取るのは主オーバーレイ1つのみとする。主オーバーレイはプライマリモニター上に配置する。
- すべてのオーバーレイを表示した後、主オーバーレイがフォーカスを取得する。取得できない場合は選択を破棄してオーバーレイを閉じる。
- 主オーバーレイ以外は表示専用とし、マウス入力を遮断する。キーボード入力は受け取らない。
- 主オーバーレイへのフォーカス取得は、Phase 1で確認する（15章）。

### 5.2 表示デザイン

- 第1段階のセルラベル：各有効セルの中央に、列キーと行キーを組み合わせた2文字ラベル（例：QY, WU）を直接表示する。これにより画面端への視線移動を行わずに、クリック目標位置の直上で必要な打鍵キーを即座に確認できる。
- 第2段階では文字ラベルを表示せず、有効セルのうち中央に最も近いセルを初期選択する。選択セルは半透明の赤でハイライトし、矢印キーで移動する。
- セルが複数モニターにまたがる場合、ラベルは所有するモニターのオーバーレイだけに描画する。
- 選択領域のないセルのラベルは表示しない。そのキーは無視する。
- 選択領域の外側は暗くし、選択領域を明るく表示する。外接矩形の枠線も表示する。
- グリッド線は細く、視認性を損なわない色にする。
- 第2段階で確定キーを押すと、選択カーソルのセルを確定し、`Enter`は左、`Shift+Enter`は右、`Ctrl+Enter`は中（ホイール）クリックする。
- 連続モードがONの間は、プライマリモニターのオーバーレイに「Continuous Mode」と表示する。表示は簡易的なテキストバッジとし、選択操作を妨げない。
- While a range anchor is active, a high-contrast green ring and crosshair are drawn at the anchor's exact physical-pixel coordinate. The marker is drawn by the monitor that contains the point, including when it is not the primary monitor.
- While a range anchor is active, the primary overlay shows the status badge `Range: Enter to select`. Status badges stack without overlapping, and an active continuous mode is shown below the range badge.
- While Scroll Mode is active, the primary overlay shows the status badge `Scroll Mode`. Continuous mode remains visible below it when enabled.
- 色・透明度は初期バージョンでは固定値とする。背景色に応じた自動色変更は行わない。

### 5.3 表示終了

以下の場合にオーバーレイを終了する。

- 通常モードでのクリックの確定または完了
- Escによるキャンセル（連続モードを含む）
- オーバーレイがフォーカスを失い、可視の他アプリケーションへ移った場合
- モニター構成またはDPIの変更
- アプリケーションの異常終了

連続モードでは、クリック送信のために一時的にオーバーレイを非表示にした後、第1段階のグリッドを再表示して選択を継続する。再表示に失敗した場合は選択を破棄して待機する。

フォーカスが自プロセスのウィンドウ、または非表示のヘルパーウィンドウ（IME/TSFのMSCTFIME UIなど）へ移った場合は、ユーザー操作による切り替えとみなさない。この場合は選択状態を破棄せず、主オーバーレイのフォーカスを再取得する。可視の他アプリケーションへフォーカスが移った場合は、フォーカスを奪い返さず、選択状態を破棄して終了する。

フォーカス喪失の判定では、キーボードフォーカスだけでなく、ウィンドウおよびアプリケーションのアクティブ状態も監視する。遷移が完了した後に主オーバーレイが前景ウィンドウかつキーボードフォーカスを持つことを確認し、そうでなければ上記の再取得または終了を行う。グリッドを表示したままフォーカスだけが他のウィンドウへ移る状態を残さない。

モニター構成・DPIが変わった場合は、選択状態を破棄する。

---

## 6. マウス入力の扱い

### 6.1 基本方針

オーバーレイ表示中は、物理マウス入力を完全に無視する。マウス入力を選択操作に利用しない。

### 6.2 入力遮断

- 主オーバーレイと表示専用オーバーレイのすべてがマウス入力を受け止める。
- 背後のアプリケーションにマウス入力を伝えない。
- マウスの移動・クリック・ホイールを選択操作に使用しない。
- グローバルマウスフックは使用しない。

### 6.3 最終クリック

1. `Enter`, `Shift+Enter`, or `Ctrl+Enter` in Stage 2, or after Stage 1, resolves the target coordinate (Section 4.4) and click kind.
2. Hide all overlays without activating another window and wait for the hide to complete (wait for the Dispatcher render cycle).
3. Identify the root window under the target coordinate and try to activate it. If activation fails, continue only when the window under the coordinate is still the identified target.
4. Verify immediately before dispatch that the window under the target coordinate still belongs to the identified target window. If it does not, do not send the click.
5. Convert the target coordinate to an absolute virtual-screen coordinate (Section 12.1).
6. Use `SendInput` to move the pointer and send the selected mouse button down and up.
7. If the `SendInput` return count does not match the requested count, report failure through a tray notification, including UIPI blocking.
8. 通常モードでは選択モードを終了して待機に戻る。連続モードではクリック入力の処理を待ってからオーバーレイを再表示し、第1段階から選択を再開する。

For a range confirmation, steps 2-4 are applied to both the anchor and endpoint:

1. Hide all overlays without closing the selection session.
2. Resolve the root window under the anchor and the root window under the endpoint. If either is missing or the roots differ, send no input, clear the range anchor, restore the Stage 2 overlay, and report the reason.
3. Activate the shared root window and verify that both endpoints still belong to it immediately before dispatch.
4. Dispatch a normal left click at the anchor, then a Shift+left click at the endpoint. Shift is held only for the endpoint click and is released in the same input batch.
5. If `SendInput` is partial, release the synthetic left button and Shift before reporting failure.
6. On success, follow the normal/continuous-mode completion rules. On dispatch failure, restore the pre-selection foreground window and report failure.

For Scroll Mode input, steps 2-4 of the normal click flow are applied to the scroll target:

1. Hide all overlays without closing the selection session.
2. Resolve and activate the root window under the current Stage 2 target, then verify the target still belongs to that root immediately before dispatch.
3. Move the pointer to the target and send `MOUSEEVENTF_WHEEL` or `MOUSEEVENTF_HWHEEL` through `SendInput`. Arrow keys use one notch; Page keys use five notches. Positive vertical notches scroll up and positive horizontal notches scroll right.
4. Restore the same Stage 2 overlays and reclaim focus. Do not complete the selection and do not change continuous mode.
5. If resolution, activation, or dispatch fails, send no wheel input when the target is not valid, exit Scroll Mode, restore Stage 2, and report the reason. A partial wheel input has no modifier state to release.

- 連続モードでは、クリック送信後に主オーバーレイがフォーカスを取得し直す。Escで終了した場合は、直前にクリックしたウィンドウへフォーカスを戻す。
- Do not restore the pointer to its original position after the click.
- Do not restore the pre-selection window before dispatch. The pre-selection window can cover the selected coordinate after it is raised and receive the click instead of the window under the target.
- Do not restore the pre-selection window after a successful click; this can undo target activation before it processes the click. If the click is not sent or dispatch fails, restore the pre-selection window.
- Transient races with physical mouse input are acceptable in the initial version.

---

## 7. キーボード入力

### 7.1 入力取得

- 主オーバーレイでキーボード入力を受け取る。
- 通常の選択操作のためにグローバルキーボードフックは使用しない。
- 表示中は主オーバーレイのIMEを無効化し（ImmAssociateContext(hwnd, IntPtr.Zero)）、表示終了時に元の状態に戻す。
- キーの判定は文字ではなくキーコードで行う。第1段階の列・行キーは3.3の対応表で番号に変換し、第2段階は矢印キーと確定キーで操作する。IMEが処理したキーは元のキーコードで判定する。
- キーリピート（IsRepeat）による入力は無視する。
- Tabは修飾キーなしの場合のみ連続モードの切り替えに使う。キーリピートは無視する。修飾キー付きのTabは連続モードを切り替えない。
- `R` activates range selection only in Stage 2 with no modifiers. Unmodified `R` in Stage 1 remains an ordinary selection key, and modified `R` is treated as an ordinary letter key.
- Plain `PageUp` and `PageDown` enter Scroll Mode in Stage 2 and scroll by five notches. Modified Page keys are consumed without entering or scrolling. In Scroll Mode, unmodified arrow keys are interpreted as scroll commands instead of cursor movement.
- While Scroll Mode is active, letter keys, Tab, Backspace, R, and confirmation keys are ignored. Esc exits Scroll Mode before the normal cancellation behavior runs.

### 7.2 ホットキー

- ホットキーはRegisterHotKeyで登録する（初期値：Ctrl + Shift + Space）。
- 登録に失敗した場合は、トレイ通知で知らせる。常駐は継続し、トレイメニューからの終了は可能とする。初期バージョンには設定画面がないため、ホットキーの変更は将来機能とする。
- 待機中にホットキーを押すと選択を開始する。選択中に再度押すと、現在の選択を破棄して履歴0個の状態から選択を再開する。
- クリック実行中にホットキーを押した場合は無視する。
- 多重起動は名前付きMutexで防止する。2つ目の起動は「既に起動しています」のトレイ通知を表示して終了する。

---

## 8. 状態と遷移

### 8.1 状態

- **待機**：オーバーレイなし。ホットキーを待つ。
- **選択中**：オーバーレイ表示中。第1段階の打鍵履歴（0〜2個）と、第2段階の選択カーソルを持つ。
- **連続モード**：選択中のセッション単位のフラグ。ONの間は確定後も待機に戻らず、第1段階から選択を再開する。Esc、ホットキーによる再開始、フォーカス喪失、モニター変更でOFFに戻る。
- **範囲選択中**：選択中のセッション単位のフラグ。Stage 2で`R`を押した後にONになり、固定されたアンカー座標を保持する。`Enter`で範囲を実行するか、`Esc`でアンカーを消すまで継続する。
- **Scroll Mode**：A Stage 2 sub-mode entered with `PageUp` or `PageDown`. Arrow and Page keys send wheel input to the current target. `Esc` exits to Stage 2; continuous mode is retained.
- **クリック実行中**：第2段階または第1段階後の確定キーで確定座標を決めた後。オーバーレイ非表示からクリック送信までの間。

第1段階では履歴0個のとき列キー、履歴1個のとき行キーを受け付ける。Backspace時は履歴から選択領域を再計算する。第2段階の矢印移動は打鍵履歴に含めない。連続モードの切り替えは打鍵履歴と選択カーソルを変更しない。範囲アンカーは打鍵履歴や第2段階カーソルとは独立して保持する。Scroll Modeも打鍵履歴と第2段階カーソルを変更せず、範囲アンカーとは同時に有効にならない。

### 8.2 遷移表

| 現在の状態 | イベント | 次の状態 | 動作 |
|---|---|---|---|
| 待機 | ホットキー | 選択中（履歴0個） | 列数・行数を算出し、オーバーレイを表示 |
| 待機 | その他すべて | 待機 | 無視 |
| 選択中（履歴0個） | 有効な列キー | 選択中（履歴1個） | 列を履歴に追加し、列をハイライト |
| 選択中（履歴1個） | 有効な行キー | 選択中（履歴2個） | 行を履歴に追加し、選択領域を第2段階の5×5グリッドへ進める |
| 選択中 | 無効な列・行キー | 変化なし | 無視 |
| 選択中 | Tab（修飾なし） | 選択中 | 連続モードを切り替える。履歴と選択カーソルは維持 |
| 選択中（履歴2個） | 有効セルへの矢印移動 | 選択中 | 次の有効セルへ選択カーソルを移動。空白領域のセルは飛び越す |
| 選択中（履歴2個） | 境界・無効セル方向への矢印移動 | 変化なし | 無視 |
| Selecting (history length 2) | `Enter` | Executing click | Confirm the cursor cell with a left click |
| Selecting (history length 2) | `Shift+Enter` | Executing click | Confirm the cursor cell with a right click |
| Selecting (history length 2) | `Ctrl+Enter` | Executing click | Confirm the cursor cell with a middle (wheel) click |
| Selecting (history length 2, no range anchor) | `R` (no modifiers) | Selecting (range active) | Capture the current target as the range anchor |
| Selecting (range active) | Arrow keys | Selecting (range active) | Move the endpoint cursor; the anchor stays fixed |
| Selecting (range active, history length 2) | `Enter` | Executing range | Validate both endpoints, then send left click + Shift+left click |
| Selecting (range active) | `Shift+Enter`, `Ctrl+Enter` | No change | Ignore; range execution uses unmodified Enter |
| Selecting (range active, history length 0 or 1) | `Enter` | No change | Ignore until Stage 2 is active |
| Selecting (range active) | `R` | No change | Ignore; the anchor is already set |
| Selecting (range active) | `Esc` | Selecting (no range) | Clear the anchor and keep the session open |
| Selecting (range active) | Backspace | Selecting | Undo one Stage 1 key when possible; keep the anchor while recalculating the region |
| Selecting (history length 2, no range) | `PageUp` / `PageDown` (no modifiers) | Selecting (scroll active) | Enter Scroll Mode and send a five-notch page scroll |
| Selecting (scroll active) | Arrow keys | Selecting (scroll active) | Send one vertical or horizontal wheel notch; do not move the cursor |
| Selecting (scroll active) | `PageUp` / `PageDown` | Selecting (scroll active) | Send a five-notch vertical wheel scroll |
| Selecting (scroll active) | `R`, `Tab`, `Backspace`, `Enter` | Selecting (scroll active) | Ignore without changing the mode or cursor |
| Selecting (scroll active) | `Esc` | Selecting (Stage 2, no scroll) | Exit Scroll Mode and keep the selection session open |
| Selecting (scroll active) | Scroll target failure | Selecting (Stage 2, no scroll) | Send no input when the target is invalid; restore Stage 2 and report the error |
| Selecting (range active) | `PageUp` / `PageDown` | Selecting (range active) | Ignore; Scroll Mode is unavailable while a range anchor is active |
| Selecting (history length 2) | Backspace | 選択中（履歴1個） | 第1段階へ戻り、領域を再計算 |
| 選択中（履歴0・1個） | Backspace | 選択中 | 履歴の末尾を取り消す。履歴が空なら変更なし |
| Selecting (history length 0 or 1) | Any confirmation key | No change | Ignore |
| 選択中 | Esc | 待機 | 選択を破棄し、連続モードをOFFにしてオーバーレイを終了 |
| 選択中 | ホットキー | 選択中（履歴0個） | 現在の選択を破棄し、連続モードをOFFにしてオーバーレイを再表示してフォーカスを取得 |
| クリック実行中 | ホットキー | 変化なし | 無視 |
| 選択中 | フォーカス喪失（可視の他プロセス） | 待機 | フォーカスを奪い返さず、選択を破棄し、連続モードをOFFにしてオーバーレイを終了 |
| 選択中 | フォーカス喪失（自プロセスまたは非表示ウィンドウ） | 選択中 | 主オーバーレイのフォーカスを再取得。再取得に失敗した場合は選択を破棄して待機 |
| 選択中 | モニター構成変更、DPI変更 | 待機 | 選択を破棄し、連続モードをOFFにしてオーバーレイを終了 |
| クリック実行中 | SendInput成功（通常モード） | 待機 | 通常状態に戻る |
| クリック実行中 | SendInput成功（連続モード） | 選択中（履歴0個） | 第1段階のオーバーレイを再表示し、連続モードを維持 |
| クリック実行中 | SendInput失敗（通常モード） | 待機 | トレイ通知 |
| クリック実行中 | SendInput失敗（連続モード） | 選択中（履歴0個） | トレイ通知し、第1段階のオーバーレイを再表示 |
| 範囲実行中 | Endpoint validation failure | Selecting (Stage 2, no range) | Send no input, clear the anchor, restore the overlay, and report an error |
| 範囲実行中 | SendInput success (normal mode) | 待機 | Complete the range and close the overlays |
| 範囲実行中 | SendInput success (continuous mode) | 選択中（履歴0個） | Reopen Stage 1 and keep continuous mode |
| 範囲実行中 | SendInput failure | 待機 or 選択中（履歴0個） | Release synthetic input, report an error, and apply the normal/continuous completion rule |
| Scroll Mode execution | SendInput success | Selecting (scroll active) | Restore the same Stage 2 overlays and keep Scroll Mode active |
| Scroll Mode execution | SendInput failure or target failure | Selecting (Stage 2, no scroll) | Restore Stage 2, exit Scroll Mode, and report the error |
| クリック実行中 | 入力・構成変更のイベント | クリック実行中 | 無視 |

連続モードがONの場合、確定キーによるクリック実行中からは、クリック完了後に待機ではなく選択中（履歴0個）へ戻る。

### 8.3 異常時の後始末

アプリケーションが例外で終了する場合も含め、以下を行う。

- 全オーバーレイを非表示にする。
- IMEの関連付けを元に戻す。
- 登録したホットキーを解除する。
- トレイアイコンを削除する。

---

## 9. 常駐・終了

- KeyGridは常駐型アプリケーションとする。
- 常駐中は、非表示のメッセージウィンドウでホットキーを監視する。
- トレイアイコンの右クリックメニューから終了できる。ホットキー登録に失敗している場合は、メニューにその旨を表示する。
- 右クリックメニューは、項目の選択、メニュー外のクリック、Escのいずれかで閉じる。
- 初期バージョンでは設定画面を設けない。
- 自動起動は初期バージョンの必須機能としない。

---

## 10. 設定・拡張方針

初期バージョンでは設定値を固定する。

| 項目 | 初期値 |
|---|---|
| 起動ホットキー | Ctrl + Shift + Space |
| 段階数 | 2（固定） |
| 列数・行数 | 第1段階は画面寸法から自動算出、第2段階は固定5×5（4.1） |
| 選択キー | 3.3の割り当て規則に従い、列キーと行キーを重複なく割り当てる（最大26キー） |
| Confirmation keys | `Enter` for left click; `Shift+Enter` for right click; `Ctrl+Enter` for middle (wheel) click |
| カーソル移動 | 第2段階で矢印キー。有効セルを順に移動 |
| 戻るキー | Backspace（第1段階の打鍵履歴を戻す） |
| キャンセルキー | Esc（Scroll Modeと範囲アンカーを先に解除し、連続モードでも終了） |
| 連続モード | 初期値OFF。グリッド表示中にTabでON/OFF。ONの間は確定後も第1段階へ戻る |
| Click kinds | Left, right, and middle (wheel) clicks |
| Range selection | Stage 2で`R`。アンカーは固定し、`Enter`で左クリック＋Shift+左クリック。Escでアンカーを解除 |
| Scroll Mode | Stage 2でPageUp/PageDown。矢印は1ノッチ、Pageは5ノッチ。EscでStage 2へ戻る |
| 確定後の動作 | 通常モードはオーバーレイ終了。連続モードは第1段階へ戻る。カーソルは元の位置に戻さない |

将来、以下の機能を追加できる構造とする。

- ホットキー変更
- 段階数の変更（3段階にして精度を上げる）
- 選択キー配置の変更（ホームポジション寄りの配置など）
- 第2段階グリッドサイズの変更
- Double-click
- ドラッグ操作
- スクロール操作
- 表示デザインの変更

---

## 11. 非機能要件

### 11.1 性能

以下は目標値とする。Phase 1で実測し、必要に応じて改訂する。

- ホットキー押下からオーバーレイ表示完了まで200ms以内（単一モニター）
- キー押下から表示更新まで100ms以内（4Kの第1段階160セルを代表ケースとする）
- 待機時のCPU使用率は平均0.1%未満
- 待機時のワーキングセットは100MB以下
- 選択モード以外では画面更新を行わない

### 11.2 安定性

- 不正な座標へのクリックを防止する。確定座標は、モニター表示領域内であることを検証してから使用する。
- Range execution validates both endpoints after the overlays are hidden. If they resolve to different top-level windows, no input is sent and the range is cancelled with an error.
- A partial range `SendInput` releases the synthetic left button and Shift so the user's keyboard and mouse state are not left modified.
- Scroll Mode verifies the target window after hiding the overlays and before sending wheel input. If the window cannot be resolved or changes after activation, no wheel input is sent and Stage 2 is restored.
- Scroll input uses no modifier keys or mouse buttons, so a partial dispatch cannot leave synthetic input state pressed.
- 選択中にモニター構成またはDPIが変わった場合は、選択を破棄して待機状態に戻る。変更はWM_DISPLAYCHANGEとWM_DPICHANGEDで検出する。メッセージ専用ウィンドウにWM_DPICHANGEDが届かない場合があるため、各オーバーレイウィンドウでも同じメッセージを監視する。
- オーバーレイを各モニターへ初期配置する際に発生するWM_DPICHANGEDは、構成変更とみなさない。初期配置の完了後に検出した変更だけを選択の破棄対象とする。
- キャンセル時・異常時に、オーバーレイ・IMEの無効化・入力遮断の状態を残さない。
- クリック実行後は確実に通常状態へ戻す。

### 11.3 セキュリティ

- 通常ユーザー権限で動作する。管理者権限は要求しない。
- Windowsのセキュリティ制約を回避する実装は行わない。
- 管理者権限で動作するウィンドウには、UIPIにより入力が届かない。その場合は6.3の手順でトレイ通知する。
- リリース成果物にはLICENSEを同梱し、PDBを含めない。SHA-256チェックサムを公開し、Authenticode署名を付与できるリリースフローを維持する。
- 配布物は管理者が書き込み権限を持つディレクトリに配置し、標準ユーザーには読み取り・実行のみを許可することを推奨する。
- ログの既定レベルはINFOとし、`KEYGRID_LOG_LEVEL`でDEBUG / INFO / WARN / ERROR / NONEを指定できる。DEBUGはウィンドウクラス名、プロセスID、ウィンドウハンドル、クリック座標、フォーカス遷移を含むため、トラブルシューティング時のみ使用する。
- ログには入力文字、ウィンドウタイトル、クリップボード、画面内容を含めない。1日ごとのログファイルは5MBを上限とし、上限到達後はその日の追加出力を停止する。ログは7日保持する。

---

## 12. 技術スタック

| 項目 | 採用技術 |
|---|---|
| OS | Windows 11（x64） |
| 言語 | C# |
| ランタイム | .NET 10（LTS） |
| UI | WPF（XAML） |
| OS連携 | Win32 API / P/Invoke |
| モニター情報 | EnumDisplayMonitors、GetMonitorInfo、GetSystemMetrics（SM_XVIRTUALSCREEN等） |
| DPI | Per-Monitor DPI Awareness V2（app.manifest）、GetDpiForMonitor |
| クリック | SendInput（MOUSEEVENTF_VIRTUALDESK） |
| ホットキー | RegisterHotKey |
| IME | ImmAssociateContext |
| ビルド | dotnet CLI（build / publish） |
| 配布 | dotnet publish -r win-x64 --self-contained false（.NET 10 Desktop Runtimeが必要） |
| ログ | %LOCALAPPDATA%\KeyGrid\logs に出力し、直近7日分を保持。既定はINFO、1ファイル5MB上限 |
| テスト | xUnit |
| バージョン管理 | Git |

### 12.1 絶対座標への変換

SendInputの絶対座標は、仮想画面を0〜65535に正規化して指定する。

- X = (x − 仮想画面の左端) × 65535 ÷ (仮想画面の幅 − 1)
- Y = (y − 仮想画面の上端) × 65535 ÷ (仮想画面の高さ − 1)
- フラグはMOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_MOVE | MOUSEEVENTF_VIRTUALDESKを指定する。
- 丸めの扱いは、Phase 1でクリック精度を確認して確定する。

### 開発環境

Visual Studio Codeをメインのエディタとして使用する。使用する拡張機能は次のとおり。

- C# Dev Kit
- C# 拡張機能
- XAML関連拡張機能
- GitHub Copilot（任意）

.NET SDK（.NET 10）をインストールし、ビルド・実行・テストはdotnet CLIから行う。Visual Studio Communityは必須とせず、必要になった場合に追加導入する。

---

## 13. 論理構成

### 13.1 モジュール

| モジュール | 責務 | 依存 |
|---|---|---|
| Application | 起動・終了管理、多重起動防止、異常時の後始末 | 各マネージャ |
| HotkeyManager | ホットキーの登録・解除、登録失敗の通知 | IHotkeyRegistrar |
| OverlayManager | 列・行ラベルとカーソルの描画、ハイライト、キー入力の受け取り、連続モード・範囲アンカー・Scroll Modeの管理 | SelectionManager、IMonitorProvider、ClickExecutor |
| SelectionManager | 第1段階の打鍵履歴、矢印カーソルの状態、状態遷移（8章）、履歴からの領域再計算、Stage 2 target resolution | GridCalculator、KeyLayout |
| GridCalculator | 第1段階の分割数最適化、境界計算、セル・有効セル、基準点と確定座標の計算 | なし（純粋計算、Win32非依存） |
| KeyLayout | 列・行キーのブロック割り当てと、キーから列・行の番号への対応 | なし（純粋計算） |
| FocusLossPolicy | フォーカス喪失時に再取得するか選択を破棄するかの判定 | なし（純粋計算） |
| MonitorProvider（Win32MonitorProvider） | モニター表示領域、実効DPI、プライマリ判定の取得、構成変更の検出 | Win32 |
| ClickExecutor | 確定座標の絶対座標への変換、SendInputの送信、範囲クリックとホイール入力の同期 | IClickSender |
| TrayManager | トレイアイコン・メニュー・通知 | なし |

### 13.2 依存関係の方針

- SelectionManagerは打鍵履歴を保持し、GridCalculatorを使って選択領域を計算する。Backspaceでは履歴を1つ減らし、領域を再計算する。
- SelectionManagerは、開始時にKeyLayout.Allocateで列キー・行キーを決定し、セッション中は同じ割り当てを使う。列キーは行キーとして受け付けず、その逆も同様とする。
- SelectionManagerは、第1段階の選択領域を第2段階の5×5グリッドへ進め、矢印移動では有効セルをスキップする。
- GridCalculatorは、幅・高さと固定の第2段階分割数を使って第1段階の最適分割数を計算し、Win32には依存しない。
- OverlayManager displays SelectionManager state (the selected region and history) and asks ClickExecutor to send the selected click kind on confirmation.
- OverlayManagerは連続モードをセッション単位で保持する。連続モードの確定後はSelectionManagerをリセットし、オーバーレイを再表示して第1段階から再開する。
- OverlayManager owns the range anchor and validates both endpoints after temporarily hiding the overlays. It asks ClickExecutor to dispatch the two-point range through IClickSender.
- OverlayManager owns Scroll Mode. It temporarily hides the overlays so the current target receives wheel input, then restores the same Stage 2 selection without completing it.
- ClickExecutor receives the target coordinate and click kind, then dispatches them through IClickSender. It also provides the render-synchronized two-point range and scroll dispatch paths.
- HotkeyManagerは操作をApplicationへ通知する。

画面表示、選択ロジック（純粋計算と状態遷移）、Windows API連携を分離する。

### 13.3 主なインターフェース

- **IMonitorProvider**：モニター情報の取得（GetMonitors）、構成変更の通知（Changed）
- **MonitorInfo**：モニターの物理表示領域、実効DPI倍率、プライマリ判定を保持する
- **IClickSender**: Sends a left, right, or middle click at a specified virtual-screen coordinate and returns whether dispatch succeeded. It also sends the two-point range sequence (left click at the anchor, Shift+left click at the endpoint) and vertical/horizontal wheel input.
- **IHotkeyRegistrar**：ホットキーの登録・解除（Register、Unregister）
- **PixelRect**：物理ピクセルの矩形。[左, 右) × [上, 下) の半開区間とする
- **KeyLayout / KeyAllocation**：左ブロックと右ブロックから重複しない列キー・行キーを割り当て、キーから列・行の番号に変換する

テストではこれらをフェイクに差し替える。

---

## 14. テスト方針

### 14.1 単体テスト（GridCalculator・KeyLayout・SelectionManager）

Win32に依存しない部分を対象とする。

- 固定5×5の第2段階を考慮した第1段階分割数の算出（例：1920×1080→16×10、3840×2160→16×10）
- すべての第1段階列数・行数の組で C + R ≤ 26 を満たすこと
- 第2段階後の最悪セル寸法を最小化すること、および同値時のタイブレークの決定性
- 第2段階が常に5×5であること
- 境界式の性質テスト：Lと分割数N（1〜26）の任意の組み合わせで、各セルの幅が⌊L÷N⌋または⌈L÷N⌉に収まる
- 左・右ブロックの内容と、C > 15・R > 11の借用を含む列キー・行キーの割り当て
- 列キーと行キーが重複せず、C + R ≤ 26のすべての組で必要数を確保できること
- 列キーを2打鍵目の行キーとして使うなど、反対側のキー集合を拒否すること
- キー入力の大文字・小文字を同一視すること
- 打鍵履歴：列→行の順で段階が進むこと、Backspaceで1つ戻ること、履歴からの再計算（同じキー割り当ての再現を含む）
- 無効セル・無効な列・行のキーが無視されること
- 第1段階の前の確定キーが無効、第1段階の後の確定キーで途中確定できること
- 第2段階で矢印キーを押すと、有効セル間でカーソルが移動し、無効セルは飛び越すこと
- 第2段階の `Enter` / `Shift+Enter` / `Ctrl+Enter` でカーソル位置が確定され、それぞれ左・右・中クリックのクリック実行状態へ移ること
- Tabで連続モードが切り替わり、修飾キー付き・リピートのTabが無視されること
- 連続モードの確定後にクリックが送信され、第1段階へ戻ること
- 連続モードをOFFに戻した場合、確定後に待機へ戻ること
- 連続モードのEsc、ホットキー再開始、フォーカス喪失、モニター変更でモードが解除されること
- 連続モードでクリック失敗時も第1段階へ戻ること
- `R` in Stage 2 captures the current target as a range anchor without changing selection state, and an unmodified `R` in Stage 1 remains a selection key
- Range anchors persist across Stage 2 cursor movement and `Backspace` into Stage 1, and `Esc` clears the anchor before exiting
- Range confirmation uses only unmodified `Enter`; modified confirmation keys are ignored while a range anchor is active
- Range execution sends a left click at the anchor and a Shift+left click at the endpoint, and follows normal/continuous completion rules
- Different top-level windows or missing endpoint windows abort range execution without sending input and restore Stage 2
- A partial range input sequence releases the synthetic left button and Shift
- Range start marker and `Range: Enter to select` badge are drawn only on the appropriate overlay(s)
- `PageUp` / `PageDown` enter Scroll Mode only in Stage 2 and dispatch a five-notch wheel scroll at the current target
- Scroll Mode arrows dispatch one vertical or horizontal notch without moving the Stage 2 cursor
- `Esc` exits Scroll Mode and keeps the Stage 2 selection active; a second `Esc` exits selection
- Scroll Mode ignores range start, continuous-mode toggle, Backspace, and confirmation keys
- Page keys are ignored while a range anchor is active, and range start is ignored while Scroll Mode is active
- Scroll target failures and partial wheel input exit Scroll Mode, restore Stage 2, and report an error
- `Scroll Mode` badge is drawn only on the primary overlay when active
- フォーカス喪失の判定：可視の他プロセスなら破棄、自プロセスまたは非表示ウィンドウなら再取得、連続再取得の上限
- 基準点（中心）の算出（偶数幅・奇数幅・1px）
- 空白領域の基準点から最近点を求める処理、同距離の場合の優先規則（y優先、次にx）
- 状態遷移表（8.2）のイベントと状態の全組み合わせ
- 絶対座標への変換式（12.1）

### 14.2 結合・実機テスト

- 単一モニター（1920×1080、2560×1440、3840×2160）
- 複数モニター、解像度の異なるモニター
- 拡大率100・125・150・175・200%、およびDPIが異なるモニターでの4.1表との一致
- 異なるDPI構成での物理ピクセル分割と、5×5カーソルの操作性
- 4K 100%（第1段階160セル）の視認性と描画時間
- モニターの上下左右がずれた配置、モニター間に空白がある配置
- 仮想画面の原点が負になる配置（左・上に配置したモニター）
- タスクバー（通常時・自動非表示時）、タイトルバー、ウィンドウ枠のクリック
- 物理マウス入力の遮断
- キーボード入力（IME ON・OFF）
- QWERTY/JIS配列での列・行キーの押しやすさと、AZERTYなど異なる配列での既知の制約
- ホットキー競合
- Esc・Backspace・確定キー
- `Shift+Enter` right-click and `Ctrl+Enter` middle-click selection, and rejection of other modifier combinations
- Left-, right-, and middle-button SendInput down/up sequences and partial-send failure handling
- フォーカス喪失（可視アプリへの切り替え、IME/TSFの非表示ウィンドウへの一時的な移動）
- 選択中のモニター抜き差し、DPI変更
- スリープ復帰、RDP接続
- フルスクリーンアプリの上での起動、仮想デスクトップの切り替え
- 管理者権限で動作するウィンドウ（UIPIによる遮断の確認）
- 列・行ラベルの視認性（モニター境界付近を含む）
- 1920×1080・4Kの第1段階での選択しやすさ、および第2段階カーソルの操作性
- クリック後の通常状態への復帰
- 連続モードのTab切り替えと「Continuous Mode」表示
- 連続モードで左・右・中クリックを繰り返し、毎回第1段階へ戻ること
- 連続モードのEsc、ホットキー再開始、フォーカス喪失、モニター変更
- 連続モードのクリック失敗時の再表示と通知
- Browser text selection using `R` + `Enter`, including selections that span lines and reversed anchors
- Range execution rejection for endpoints in different windows, and recovery to Stage 2
- Visualization of the range anchor across monitors and with continuous mode enabled
- Browser and document scrolling with Scroll Mode, including vertical pages, horizontal arrows, and repeated scrolling without losing Stage 2
- Scroll Mode target-window changes, UIPI failures, and recovery to Stage 2
- 分割数と第2段階後のセル寸法の実測（4.1の表と比較）
- クリック精度：クリック座標を記録する検証用ウィンドウを使い、確定座標と実際のクリック位置を自動比較する

---

## 15. 開発フェーズ

### Phase 1：技術検証

| 検証項目 | 合格基準 | 区分 |
|---|---|---|
| 全モニターへのオーバーレイ表示 | すべてのモニターを覆う | 必須 |
| マウス入力の遮断 | 背後のアプリがホバー・クリックを受けない | 必須 |
| キーボード入力の取得 | ホットキーから、選択キーを含むすべてのキーが主オーバーレイに届く | 必須 |
| フォーカス取得 | 他のアプリが前面にあっても、ホットキーでフォーカスを得られる（AttachThreadInputなどを含む） | 必須 |
| IME無効化 | IME ON時も列キー・行キーの選択キーが正しく届く | 必須 |
| SendInputの座標 | 仮想画面の左・上に配置したモニターでも、指定座標に一致する | 必須 |
| DPIと座標のずれ | 100・150・200%が混在する環境で、検証ウィンドウ上のずれが0pxである | 必須 |
| 非表示との同期 | 非表示の反映後にクリックし、オーバーレイ上でクリックが発生しない | 必須 |
| 操作性 | 10列×8行（1920 100%）、14列×11行（4K 100%）の配置で、ラベルを見て2打鍵の選択を正しく連続して行える | 必須 |
| 分割精度 | 4.1の代表ケースで選択カーソルを目的位置へ移動できる | 必須 |
| タスクバーの覆い | 通常時・自動非表示時とも覆える | 条件付き（不合格ならタスクバーを初期バージョンの対象外にして次へ進む） |
| 性能目標 | 11.1の目標値を満たす（超える場合は値を改訂） | Phase 2完了時に判定 |

**Go/No-Go**：「必須」の項目がすべて合格した場合にPhase 2へ進む。不合格の項目がある場合は、原因を解決するか仕様を変更してから進む。

### Phase 2：基本機能

- ホットキーによる起動・終了
- 第1段階の列・行ラベルと、第2段階の赤い選択カーソル表示
- 第1段階の2打鍵による領域選択、第2段階の矢印キー移動
- 第1段階の打鍵履歴とBackspaceによる取り消し
- Enter, Shift+Enter, and Ctrl+Enter confirmation (Stage 2 cursor or Stage 1 region)
- Escによるキャンセル
- 最終クリック（6.3の手順）

### Phase 3：マルチモニター対応

- 複数モニターの領域管理
- モニター間の空白領域の除外
- 不規則配置への対応
- DPI・解像度差への対応（物理ピクセル座標を維持する）
- キー数上限（C + R ≤ 26）と第1段階分割最適化の確認
- 分割数と第2段階後のセル寸法の実測（4.1の表と比較）

### Phase 4：常駐アプリ化

- トレイアイコン・メニュー・終了操作
- ホットキー登録失敗の通知、多重起動の防止
- 異常終了・フォーカス喪失・構成変更時の後始末
- ログ出力
- 実機での安定性確認

---

## 16. 初期バージョンの完了条件

以下をすべて満たした時点で、初期バージョン完成とする。

- Windows 11で常駐できる。
- ホットキーで選択モードを開始できる。
- 全モニターに列・行ラベルを表示できる。
- 第1段階を列・行の2打鍵で選び、第2段階を矢印キーと確定キーで確定できる。
- 列数と行数の合計が26以下で、第2段階が5×5である。
- 第2段階の選択セルを確定キーで正しくクリックできる（左・右・中）。
- 第1段階の後であれば、確定キーで選択領域を確定してクリックできる（左・右・中）。
- Backspaceで第1段階の打鍵履歴を1つずつ取り消せる。
- グリッド表示中にTabで連続モードをON/OFFできる。
- 連続モードでは確定後に第1段階のグリッドが再表示され、続けてクリックできる。
- Stage 2で`R`により範囲アンカーを設定し、`Enter`でアンカーから端点までShift+左クリックの範囲選択を実行できる。
- 範囲アンカーはBackspace後も保持され、Escで解除できる。
- 異なるウィンドウにまたがる範囲は入力せずにエラーを通知し、Stage 2へ戻る。
- 連続モードでも範囲選択を実行でき、実行後は第1段階へ戻る。
- Stage 2でPageUp/PageDownからScroll Modeへ入り、矢印とPageキーで対象ウィンドウをスクロールできる。
- Scroll Mode中はStage 2カーソルと連続モードが維持され、EscでStage 2へ戻れる。
- Scroll Modeの対象ウィンドウが取得できない場合は、入力を送らずStage 2へ戻ってエラーを通知する。
- 範囲アンカーとScroll Modeは同時に有効にならない。
- 連続モードでもEscで正常に終了できる。
- Escで正常にキャンセルできる。
- モニター間の空白領域をクリックしない。
- オーバーレイ表示中に物理マウス入力が背後のアプリに伝わらない。
- IME ON時もキー操作できる。
- フォーカス喪失時に選択状態を破棄できる。
- クリック後に通常状態へ確実に復帰できる。
- SendInput失敗時・ホットキー登録失敗時にトレイ通知で知らせる。
- トレイからアプリケーションを終了できる。
- タスクバーを対象に含める場合は、タスクバーをクリックできる。
