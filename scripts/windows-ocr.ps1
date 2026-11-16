param([Parameter(Mandatory=$true)][string]$ImagePath)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime] | Out-Null
[Windows.Storage.FileAccessMode,Windows.Storage,ContentType=WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.BitmapDecoder,Windows.Foundation,ContentType=WindowsRuntime] | Out-Null
[Windows.Graphics.Imaging.SoftwareBitmap,Windows.Foundation,ContentType=WindowsRuntime] | Out-Null
[Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime] | Out-Null
[Windows.Media.Ocr.OcrResult,Windows.Foundation,ContentType=WindowsRuntime] | Out-Null
[Windows.Globalization.Language,Windows.Globalization,ContentType=WindowsRuntime] | Out-Null
function Await-Result($Operation,[Type]$ResultType) {
 $method=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethodDefinition -and $_.GetGenericArguments().Count -eq 1 -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
 $task=$method.MakeGenericMethod($ResultType).Invoke($null,@($Operation))
 $task.Wait()
 return $task.Result
}
$file=Await-Result ([Windows.Storage.StorageFile]::GetFileFromPathAsync($ImagePath)) ([Windows.Storage.StorageFile])
$stream=Await-Result ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
try {
 $decoder=Await-Result ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
 $bitmap=Await-Result ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
 $engine=[Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage([Windows.Globalization.Language]::new('en-US'))
 if ($null -eq $engine) { throw 'English Windows OCR language support is unavailable' }
 $result=Await-Result ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
 $words=@()
 $text=[Text.StringBuilder]::new()
 $offset=0
 foreach ($line in $result.Lines) {
  if ($text.Length -gt 0) { [void]$text.Append("`n"); $offset++ }
  $first=$true
  foreach ($word in $line.Words) {
  if (-not $first) { [void]$text.Append(' '); $offset++ }
  $first=$false
  $start=$offset
  foreach ($character in $word.Text.ToCharArray()) { if (-not [char]::IsLowSurrogate($character)) { $offset++ } }
  [void]$text.Append($word.Text)
  $r=$word.BoundingRect
  $words+=@{text=$word.Text;start=$start;end=$offset;x=$r.X;y=$r.Y;width=$r.Width;height=$r.Height}
 } }
 [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
 @{words=$words;angle=$result.TextAngle;text=$text.ToString();engine='Windows OCR en-US'} | ConvertTo-Json -Depth 5 -Compress
 $bitmap.Dispose()
} finally { $stream.Dispose() }
