# download-ner-model.ps1
# Downloads and converts yashpwr/resume-ner-bert-v2 to ONNX int8 for OnnxNerDetector.
#
# Prerequisites:
#   pip install optimum[onnxruntime] transformers torch
#
# Output:  models/resume-ner/model.onnx + vocab.txt
# Config:  set OnnxNer.ModelPath in appsettings.json / user secrets

param(
    [string]$OutputDir = ".\models\resume-ner",
    [string]$Model     = "yashpwr/resume-ner-bert-v2"
)

$ErrorActionPreference = "Stop"

Write-Host "Exporting $Model to ONNX int8..." -ForegroundColor Cyan

# Check Python is available
if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
    Write-Error "Python not found. Install Python 3.10+ and run: pip install optimum[onnxruntime] transformers torch"
}

# Export via Optimum
optimum-cli export onnx `
    --model $Model `
    --task token-classification `
    --quantize int8 `
    --output $OutputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "optimum-cli export failed. Run: pip install -U optimum[onnxruntime]"
}

# Verify outputs
$modelFile = Join-Path $OutputDir "model.onnx"
$vocabFile  = Join-Path $OutputDir "vocab.txt"

if (-not (Test-Path $modelFile)) { Write-Error "model.onnx not found in $OutputDir" }
if (-not (Test-Path $vocabFile))  { Write-Error "vocab.txt not found in $OutputDir" }

$sizeKb = [math]::Round((Get-Item $modelFile).Length / 1KB)
Write-Host "Done. model.onnx = ${sizeKb} KB" -ForegroundColor Green
Write-Host ""
Write-Host "Configure in appsettings or user secrets:" -ForegroundColor Yellow
Write-Host "  dotnet user-secrets set `"OnnxNer:ModelPath`" `"$(Resolve-Path $modelFile)`"" -ForegroundColor Yellow
Write-Host "  dotnet user-secrets set `"OnnxNer:VocabPath`" `"$(Resolve-Path $vocabFile)`"" -ForegroundColor Yellow
