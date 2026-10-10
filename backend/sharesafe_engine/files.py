"""Import files and produce new, flattened sanitized copies.

Original DOCX/PDF containers are never repackaged with hidden sensitive data.
Text-only export intentionally drops embedded images, macros, comments,
headers, custom XML, document metadata and original layout.
"""
from __future__ import annotations
import io,zipfile
from pathlib import Path
from PIL import Image,ImageDraw,ImageOps
from docx import Document
from .core import MAX_TEXT

MAX_BYTES=12*1024*1024
Image.MAX_IMAGE_PIXELS=16_000_000

def ingest(name,data,ocr=True):
    if not isinstance(data,bytes) or not data or len(data)>MAX_BYTES:raise ValueError('Upload a nonempty file smaller than 12 MiB')
    ext=Path(name).suffix.lower();warnings=[];words=[];image=None
    if ext in {'.txt','.md','.csv','.json'}:
        try:text=data.decode('utf-8-sig')
        except UnicodeDecodeError:raise ValueError('Text files must use UTF-8')
        if '\x00' in text:raise ValueError('Binary content is not a text file')
        kind='text'
    elif ext=='.docx':
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            if len(archive.infolist())>3000 or sum(x.file_size for x in archive.infolist())>40*1024*1024:raise ValueError('DOCX expands beyond the safe import limit')
            if 'word/document.xml' not in archive.namelist():raise ValueError('Not a DOCX document')
        doc=Document(io.BytesIO(data))
        from docx.oxml.ns import qn
        # Extract all main-body paragraphs, including table-cell paragraphs.
        pieces=[]
        for p in doc.element.body.iter(qn('w:p')):
            pieces.append(''.join(t.text or '' for t in p.iter(qn('w:t'))))
        for section in doc.sections:
            for part in (section.header,section.footer):
                pieces += [p.text for p in part.paragraphs if p.text]
        text='\n'.join(pieces);kind='document'
        warnings.append('DOCX export is a new text-only copy. Images, comments, tracked changes, metadata and original formatting are discarded; inspect the new copy before sharing.')
    elif ext=='.pdf':
        import pymupdf
        if not data.startswith(b'%PDF-'):raise ValueError('Not a PDF file')
        with pymupdf.open(stream=data,filetype='pdf') as doc:
            if doc.is_encrypted:raise ValueError('Encrypted PDFs are not supported')
            if len(doc)>100:raise ValueError('PDF exceeds 100 pages')
            text='\n\n'.join(p.get_text() for p in doc)
            if any(not p.get_text().strip() for p in doc):warnings.append('At least one PDF page has no extractable text. A text-only export discards these pages and all images. Review completeness.')
        kind='document';warnings.append('PDF export is a new text-only PDF. Images, annotations, attachments and original layout are discarded rather than retained unscanned.')
    elif ext in {'.png','.jpg','.jpeg','.webp'}:
        image=ImageOps.exif_transpose(Image.open(io.BytesIO(data))).convert('RGB')
        image.load();kind='image'
        text=''
        if ocr:warnings.append('OCR is supplied by the native macOS Vision client')
        warnings.append('OCR can miss text. Inspect the entire image and add manual rectangles before export. PNG export removes source metadata.')
    else:raise ValueError('Supported formats: TXT, MD, CSV, JSON, DOCX, PDF, PNG, JPEG and WEBP')
    if len(text)>MAX_TEXT:raise ValueError('Extracted text exceeds 200,000 characters')
    if not text.strip() and kind!='image':raise ValueError('No text could be extracted; file export is disabled')
    return {'text':text,'kind':kind,'warnings':warnings,'words':words,'image':image,'name':Path(name).name}

def boxes_for_findings(findings,words):
    boxes=[]
    for f in findings:
        matches=[w for w in words if w['start']<f.end and f.start<w['end']]
        for w in matches:boxes.append({'finding_id':f.id,'x':max(0,w['x']-2),'y':max(0,w['y']-2),'width':w['width']+4,'height':w['height']+4})
    return boxes

def redact_image(image,boxes):
    output=image.copy();output.info.clear();draw=ImageDraw.Draw(output)
    for b in boxes:
        try:coords=[float(b[k]) for k in ('x','y','width','height')]
        except (KeyError,ValueError,TypeError):raise ValueError('Invalid redaction rectangle')
        import math
        if not all(math.isfinite(x) for x in coords) or coords[2]<=0 or coords[3]<=0:raise ValueError('Invalid redaction rectangle')
        x,y,w,h=coords
        if x<0 or y<0 or x>image.width or y>image.height:raise ValueError('Rectangle is outside the image')
        draw.rectangle((int(x),int(y),min(image.width,int(x+w+1)),min(image.height,int(y+h+1))),fill=(0,0,0))
    out=io.BytesIO();output.save(out,format='PNG');return out.getvalue()

def export_text(text,format='txt'):
    if format=='txt':return text.encode('utf-8'),'text/plain; charset=utf-8','sanitized.txt'
    if format=='docx':
        out=io.BytesIO();doc=Document()
        for line in text.split('\n'):doc.add_paragraph(line)
        doc.core_properties.author='';doc.core_properties.last_modified_by='';doc.core_properties.title='Sanitized document'
        doc.save(out);return out.getvalue(),'application/vnd.openxmlformats-officedocument.wordprocessingml.document','sanitized.docx'
    if format=='pdf':
        import pymupdf
        from textwrap import wrap
        doc=pymupdf.open();page=None;y=800
        for paragraph in text.split('\n'):
            # Base-14 font export supports Latin-1. Unsupported characters are
            # escaped explicitly, not silently removed.
            printable=paragraph.encode('latin-1','backslashreplace').decode('latin-1')
            for line in wrap(printable,width=95,replace_whitespace=False) or ['']:
                if y>790:page=doc.new_page(width=595,height=842);y=45
                page.insert_text((35,y),line,fontsize=10,fontname='helv');y+=15
        data=doc.tobytes(garbage=4,deflate=True);doc.close();return data,'application/pdf','sanitized.pdf'
    raise ValueError('Unknown export format')
