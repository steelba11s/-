from pathlib import Path
import importlib.util, json
import pypdfium2 as pdfium
from pypdf import PdfReader

ROOT=Path(__file__).resolve().parent
renderer_path=Path('C:/Users/Artur/.codex/plugins/cache/openai-primary-runtime/documents/26.909.12148/skills/documents/render_docx.py')
spec=importlib.util.spec_from_file_location('packaged_renderer',renderer_path)
renderer=importlib.util.module_from_spec(spec);spec.loader.exec_module(renderer)
# Use the installed Word engine's verified export because no bundled LibreOffice
# exists in this Windows runtime. Reuse packaged rasterize and output naming.
renderer.convert_to_pdf=lambda doc_path,*a,**k:(str(ROOT/(Path(doc_path).stem+'.pdf')),'Word COM export')
def pdfium_convert(pdf_path,dpi,output_folder,**kwargs):
    doc=pdfium.PdfDocument(pdf_path);paths=[]
    for i in range(len(doc)):
        path=Path(output_folder)/f'page-{i+1}.png'
        page=doc[i];image=page.render(scale=dpi/72).to_pil();image.save(path);paths.append(str(path))
    return paths
renderer.convert_from_path=pdfium_convert
report={}
for key,prefix in [('trainer','Тренажёр')]:
    docx=next((ROOT.parents[1]/'Материалы для защиты').glob(prefix+'*.docx'))
    pages=renderer.rasterize(str(docx),str(ROOT/key),110,False,False)
    pdf=PdfReader(ROOT/(docx.stem+'.pdf'))
    report[key]={'pages':len(pages),'characters':[len(p.extract_text() or '') for p in pdf.pages], 'last_lines':[(p.extract_text() or '').splitlines()[-3:] for p in pdf.pages]}
    (ROOT/(key+'-extracted.txt')).write_text('\n\n'.join(f'PAGE {i+1}\n{p.extract_text()}' for i,p in enumerate(pdf.pages)),encoding='utf-8')
(ROOT/'page_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:{'pages':v['pages'],'chars':v['characters']} for k,v in report.items()},ensure_ascii=False))
