$ErrorActionPreference='Stop'
$guideRoot=Join-Path $PSScriptRoot '..\..\Материалы для защиты'
$wordApp=$null
$wordDoc=$null
try {
    $wordApp=New-Object -ComObject Word.Application
    $wordApp.Visible=$false
    $wordApp.DisplayAlerts=0
    foreach ($guideFile in (Get-ChildItem -LiteralPath $guideRoot -Filter '*.docx')) {
        $pdfPath=Join-Path $PSScriptRoot ($guideFile.BaseName+'.pdf')
        $wordDoc=$wordApp.Documents.Open($guideFile.FullName,$false,$true)
        $wordDoc.Repaginate()
        $wordDoc.ExportAsFixedFormat($pdfPath,17)
        $wordDoc.Close($false)
        $wordDoc=$null
        Get-Item -LiteralPath $pdfPath | Select-Object Name,Length
    }
} finally {
    if ($wordDoc -ne $null) {$wordDoc.Close($false)}
    if ($wordApp -ne $null) {$wordApp.Quit()}
}
