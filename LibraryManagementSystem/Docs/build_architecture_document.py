from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from docx import Document
from docx.shared import Inches, Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.section import WD_SECTION
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

ROOT = Path(__file__).resolve().parent
ASSETS = ROOT / ".architecture_assets"
OUTPUT = ROOT / "LibraryForge_Architecture.docx"
ASSETS.mkdir(parents=True, exist_ok=True)

TEAL = "006D77"; TEAL2 = "00A8B5"; AMBER = "F4A261"; NAVY = "0F172A"
SLATE = "1E2937"; TEXT = "1F2937"; MUTED = "64748B"; PALE = "E6F4F5"
GREEN = "10B981"; RED = "EF4444"; WHITE = "FFFFFF"; LINE = "CBD5E1"

def font(size=30, bold=False):
    candidates = [Path("C:/Windows/Fonts/seguisb.ttf" if bold else "C:/Windows/Fonts/segoeui.ttf"), Path("C:/Windows/Fonts/arial.ttf")]
    for p in candidates:
        if p.exists(): return ImageFont.truetype(str(p), size)
    return ImageFont.load_default()

def box(draw, xy, title, detail="", fill=WHITE, outline=TEAL, title_color=TEXT):
    draw.rounded_rectangle(xy, radius=18, fill="#"+fill, outline="#"+outline, width=4)
    x1,y1,x2,y2=xy
    draw.text((x1+22,y1+18), title, fill="#"+title_color, font=font(28,True))
    if detail:
        lines=[]
        for raw in detail.split("\n"):
            words=raw.split(); line=""
            for w in words:
                test=(line+" "+w).strip()
                if draw.textlength(test,font=font(21)) > x2-x1-44:
                    lines.append(line); line=w
                else: line=test
            if line: lines.append(line)
        draw.multiline_text((x1+22,y1+60),"\n".join(lines),fill="#"+MUTED,font=font(21),spacing=8)

def arrow(draw, start, end, color=TEAL, label=None):
    draw.line([start,end],fill="#"+color,width=5)
    import math
    a=math.atan2(end[1]-start[1],end[0]-start[0]); s=18
    pts=[end,(end[0]-s*math.cos(a-.55),end[1]-s*math.sin(a-.55)),(end[0]-s*math.cos(a+.55),end[1]-s*math.sin(a+.55))]
    draw.polygon(pts,fill="#"+color)
    if label:
        mx=(start[0]+end[0])//2;my=(start[1]+end[1])//2
        draw.rounded_rectangle((mx-90,my-18,mx+90,my+18),8,fill="#FFFFFF")
        draw.text((mx,my),label,anchor="mm",fill="#"+MUTED,font=font(17,True))

def canvas(title, subtitle):
    im=Image.new("RGB",(1600,900),"#F8FAFC");d=ImageDraw.Draw(im)
    d.rectangle((0,0,1600,110),fill="#"+NAVY)
    d.text((52,28),title,fill="white",font=font(38,True));d.text((52,75),subtitle,fill="#94A3B8",font=font(19))
    return im,d

def save(im,name):
    p=ASSETS/name;im.save(p,dpi=(160,160));return p

def diagram_context():
    im,d=canvas("System context","Offline-first desktop workflows with optional Google identity")
    box(d,(45,230,355,430),"Library staff","Administrator\nLibrarian",PALE,TEAL)
    box(d,(45,520,355,720),"Library member","Viewer account\nPersonal library",PALE,TEAL2)
    box(d,(540,240,950,650),"LibraryForge","WPF shell + MVVM\nCore library workflows\nLocal member session",WHITE,TEAL2)
    box(d,(1120,145,1530,315),"Google Identity","System-browser OAuth 2.0\nOpenID Connect",WHITE,AMBER)
    box(d,(1120,380,1530,550),"Local storage","SQLite database",WHITE,MUTED)
    box(d,(1120,615,1530,785),"File system","Covers, exports,\nbackups and logs",WHITE,MUTED)
    arrow(d,(355,330),(540,360),label="uses");arrow(d,(355,620),(540,545),label="uses");arrow(d,(950,300),(1120,230),AMBER,"optional");arrow(d,(950,450),(1120,465),label="EF Core");arrow(d,(950,585),(1120,700),label="reads/writes")
    return save(im,"01-system-context.png")

def diagram_layers():
    im,d=canvas("Layered application architecture","Dependency direction flows inward through services to persistence")
    layers=[("Views","XAML windows and user controls","E6F4F5",160),("ViewModels","Commands, observable state, navigation","FFFFFF",295),("Services","Catalog, loans, reservations, audit, reports, auth","FFF4E8",430),("Data + Domain","EF Core DbContext, entities, SQLite","FFFFFF",565)]
    for title,detail,fill,y in layers:
        box(d,(250,y,1350,y+105),title,detail,fill,TEAL if title!="Services" else AMBER)
        if y>160: arrow(d,(800,y),(800,y-30),MUTED)
    return save(im,"02-layered-architecture.png")

def diagram_startup():
    im,d=canvas("Startup and dependency injection","Application composition through Microsoft.Extensions.Hosting")
    xs=[60,360,680,1000,1300]; titles=[("App.OnStartup","desktop entry"),("HostBuilder","configuration + Serilog"),("DI container","DbContext factory + services"),("Initializer","schema + seed data"),("LoginWindow","authenticated shell")]
    for x,(t,s) in zip(xs,titles): box(d,(x,320,x+240,500),t,s,WHITE,TEAL)
    for x in xs[:-1]: arrow(d,(x+240,410),(x+300,410),AMBER)
    box(d,(520,620,1080,760),"Failure boundary","Startup errors are logged; database initialization occurs before UI navigation.","FFF4E8",AMBER)
    arrow(d,(800,500),(800,620),MUTED)
    return save(im,"03-startup-di.png")

def diagram_auth():
    im,d=canvas("Authentication and role-based access","Local credentials and Google identity converge on one protected session")
    box(d,(35,170,290,355),"Local sign-in","Username + password\nBCrypt + lockout",PALE,TEAL)
    box(d,(35,530,290,715),"Google sign-in","System browser\nPKCE + state + nonce","FFF4E8",AMBER)
    box(d,(385,145,735,380),"Authentication","Active account check\nAudit outcomes\nPassword policy",WHITE,TEAL)
    box(d,(385,500,735,750),"Google validation","Loopback callback\nSigned ID token\nAudience, issuer, expiry\nVerified email",WHITE,AMBER)
    box(d,(825,245,1120,485),"SessionService","Immutable identity\nRole + member link\nEight-hour default",WHITE,GREEN)
    box(d,(1210,150,1560,360),"Authorization","Map role to permissions\nEnforce in services",WHITE,TEAL2)
    box(d,(1210,520,1560,730),"Denied / rejected","Inactive or locked\nExpired session\nInvalid token or role","FFF0F0",RED)
    arrow(d,(290,260),(385,260),TEAL);arrow(d,(290,620),(385,620),AMBER);arrow(d,(735,265),(825,320),GREEN);arrow(d,(735,610),(825,410),GREEN);arrow(d,(1120,320),(1210,255),TEAL2);arrow(d,(1120,440),(1210,620),RED)
    return save(im,"04-auth-rbac.png")

def diagram_email_verification():
    im,d=canvas("Local registration email confirmation","A local Viewer session begins only after ownership of the email address is proven")
    steps=[("Register",35,"Validate profile\nand password",TEAL),("Pending account",330,"IsEmailVerified = false\nNo session",AMBER),("Send code",650,"Six random digits\nSMTP delivery",TEAL2),("Confirm",960,"Hash comparison\nExpiry + attempt limit",AMBER),("Activate",1280,"Mark verified\nCreate session",GREEN)]
    for title,x,detail,color in steps: box(d,(x,270,x+250,500),title,detail,WHITE,color)
    for a,b in zip(steps,steps[1:]): arrow(d,(a[1]+250,385),(b[1],385),a[3])
    box(d,(355,650,1245,800),"Abuse controls","10-minute expiry  |  60-second resend delay  |  5 attempts  |  only a SHA-256 code hash is stored","FFF4E8",AMBER)
    arrow(d,(1085,500),(1085,650),MUTED)
    return save(im,"04b-email-verification.png")

def diagram_settings():
    im,d=canvas("Settings and profile media","Personal data, profile images, account security, and appearance have focused boundaries")
    box(d,(600,135,1000,290),"SettingsView","Profile fields  |  photo picker  |  theme  |  password",PALE,TEAL)
    box(d,(45,390,365,610),"MemberPortalService","Session-derived MemberId\nValidation + audit\nVerified email protected",WHITE,TEAL)
    box(d,(440,390,760,610),"Profile image storage","PNG / JPEG / BMP\n5 MB maximum\nCopy to LocalAppData",WHITE,AMBER)
    box(d,(835,390,1155,610),"ThemeService","Apply Material palette\nPersist user preference",WHITE,TEAL2)
    box(d,(1230,390,1550,610),"Security dialog","Local-password accounts\nBCrypt rotation",WHITE,GREEN)
    box(d,(80,705,330,845),"SQLite","Member.PhotoPath\nAuditLog",WHITE,MUTED)
    box(d,(475,705,725,845),"ProfileImages","Managed local copies",WHITE,MUTED)
    box(d,(870,705,1120,845),"preferences.json","Per Windows account",WHITE,MUTED)
    arrow(d,(680,290),(205,390),TEAL);arrow(d,(755,290),(600,390),AMBER);arrow(d,(845,290),(995,390),TEAL2);arrow(d,(920,290),(1390,390),GREEN)
    arrow(d,(205,610),(205,705),TEAL);arrow(d,(600,610),(600,705),AMBER);arrow(d,(995,610),(995,705),TEAL2)
    return save(im,"08-settings-profile.png")

def diagram_data():
    im,d=canvas("Domain and persistence model","Primary relationships configured in LibraryDbContext")
    entities={"Genre":(55,160,325,305),"Book":(455,145,770,335),"Member":(455,560,770,750),"Loan":(965,300,1275,500),"Reservation":(965,615,1325,805),"User":(55,430,325,600),"ExternalLogin":(55,665,365,835),"AuditLog":(1320,145,1560,315)}
    details={"Genre":"1 to many Books","Book":"Inventory, metadata, covers\navailable / total copies","Member":"Identity, expiry, status\nmember code and photo","Loan":"Issue, due, return, fine\nActive / Returned / Overdue","Reservation":"Waitlist position + expiry","User":"BCrypt/local flag + role\noptional MemberId","ExternalLogin":"Provider + subject\nverified provider email","AuditLog":"Action, entity,\nactor, time"}
    for n,xy in entities.items(): box(d,xy,n,details[n],WHITE,TEAL if n in ("Book","Member") else MUTED)
    arrow(d,(325,230),(455,230),label="1:N");arrow(d,(770,270),(965,360),label="1:N");arrow(d,(770,620),(965,455),label="1:N");arrow(d,(770,305),(965,680),AMBER,"1:N");arrow(d,(770,690),(965,750),AMBER,"1:N");arrow(d,(325,515),(455,610),TEAL2,"0:1");arrow(d,(210,600),(210,665),AMBER,"1:N")
    return save(im,"05-domain-model.png")

def diagram_circulation():
    im,d=canvas("Circulation transaction flow","Availability and eligibility remain consistent under issue and return operations")
    steps=[("Request",80,"Book + member + days",TEAL),("Validate",390,"Exists, available, active, not expired",AMBER),("Transaction",740,"Decrement/increment copy; create/update loan",TEAL),("Commit",1110,"SaveChanges + commit",GREEN),("Feedback",1370,"Message + fine",TEAL2)]
    for title,x,detail,color in steps: box(d,(x,300,x+220,510),title,detail,WHITE,color)
    for a,b in zip(steps,steps[1:]): arrow(d,(a[1]+220,405),(b[1],405),a[3])
    box(d,(485,640,1115,775),"Return fine","max(0, return date - due date) × configured daily rate", "FFF4E8",AMBER)
    return save(im,"06-circulation.png")

def diagram_reservations():
    im,d=canvas("Reservation lifecycle","Staff and member commands share rules; member cancellation enforces ownership")
    steps=[("Unavailable title",45,"No unprotected copy",TEAL),("Waiting",345,"FIFO queue position",AMBER),("Ready for pickup",650,"Copy protected\n3-day pickup window",TEAL2),("Fulfilled",1010,"Issued to reserved member",GREEN),("Expired / cancelled",1300,"Release copy\npromote next",RED)]
    for title,x,detail,color in steps: box(d,(x,285,x+250,485),title,detail,WHITE,color)
    arrow(d,(295,385),(345,385),TEAL);arrow(d,(595,385),(650,385),AMBER);arrow(d,(900,350),(1010,350),GREEN);arrow(d,(900,430),(1300,430),RED)
    arrow(d,(1425,485),(1425,650),RED);arrow(d,(1425,650),(470,650),RED,label="promote next waiting member")
    box(d,(455,690,1145,845),"Promotion triggers","Book return  |  owner/staff cancellation  |  pickup deadline expiry\nMember commands derive MemberId from the active session", "FFF4E8",AMBER)
    return save(im,"07-reservations.png")

def diagram_operations():
    im,d=canvas("Reporting, backup, logging, and theming","Cross-cutting services stay isolated from screen logic")
    box(d,(600,155,1000,300),"Application services","Business workflows invoke focused infrastructure ports",PALE,TEAL)
    targets=[("ReportService","ClosedXML / QuestPDF",60,455,TEAL),("BackupService","SQLite file copy",440,455,AMBER),("AuditService","Transactional event records",820,455,GREEN),("Theme palette","Light / Dark resources",1200,455,TEAL2)]
    for t,s,x,y,c in targets: box(d,(x,y,x+330,y+175),t,s,WHITE,c)
    for start,end,color in [((660,300),(225,455),TEAL),((750,300),(605,455),AMBER),((850,300),(985,455),GREEN),((940,300),(1365,455),TEAL2)]: arrow(d,start,end,color)
    box(d,(500,715,1100,825),"Durable outputs",".xlsx  •  .pdf  •  .db backups  •  AuditLogs  •  diagnostic .log",WHITE,GREEN)
    arrow(d,(225,630),(650,715),TEAL);arrow(d,(605,630),(760,715),AMBER);arrow(d,(985,630),(900,715),GREEN)
    return save(im,"07-operations.png")

def diagram_inventory_qr():
    im,d=canvas("Physical inventory and secure internal QR","One accession per copy; no QR payload or key leaves the Windows installation")
    box(d,(45,145,340,335),"Catalog title","Book metadata\nsummary copy counts",WHITE,TEAL)
    box(d,(45,555,340,755),"Circulation","Issue exact available copy\nReturn to available\nor maintenance",WHITE,AMBER)
    box(d,(430,245,765,505),"BookCopy","AccessionCode\nStatus + condition\nQR issuance time",PALE,TEAL2)
    box(d,(430,610,765,820),"InventoryEvent","Added, loaned, returned\nmaintenance, lost, restored\nretired, QR issued",WHITE,MUTED)
    box(d,(855,140,1190,350),"SecureQrCodeService","AES-256-GCM\nauthenticated LF1 payload\nlocal PNG rendering",WHITE,TEAL)
    box(d,(1270,140,1560,350),"Installation secret","32 random bytes\nWindows DPAPI CurrentUser\nLocalApplicationData",WHITE,GREEN)
    box(d,(855,505,1190,735),"Internal validation","Decrypt + authenticate\ncheck LibraryForge-v1\ncheck installation ID\nlook up live copy",WHITE,AMBER)
    box(d,(1270,535,1560,705),"Rejected","Tampered, corrupted\nforeign installation\nor stale record", "FFF0F0",RED)
    arrow(d,(340,240),(430,315),TEAL,"1:N");arrow(d,(340,650),(430,430),AMBER,"exact copy");arrow(d,(595,505),(595,610),MUTED,"history")
    arrow(d,(765,330),(855,245),TEAL2);arrow(d,(1190,245),(1270,245),GREEN)
    arrow(d,(1015,350),(1015,505),AMBER,"scan LF1");arrow(d,(1190,620),(1270,620),RED)
    return save(im,"09-inventory-secure-qr.png")

DIAGRAMS=[diagram_context(),diagram_layers(),diagram_startup(),diagram_auth(),diagram_data(),diagram_circulation(),diagram_reservations(),diagram_operations(),diagram_email_verification(),diagram_settings(),diagram_inventory_qr()]

def set_cell_shading(cell,fill):
    tcPr=cell._tc.get_or_add_tcPr();shd=tcPr.find(qn('w:shd'))
    if shd is None: shd=OxmlElement('w:shd');tcPr.append(shd)
    shd.set(qn('w:fill'),fill)

def set_cell_margins(cell,top=80,start=120,bottom=80,end=120):
    tc=cell._tc;tcPr=tc.get_or_add_tcPr();m=tcPr.first_child_found_in('w:tcMar')
    if m is None: m=OxmlElement('w:tcMar');tcPr.append(m)
    for k,v in [('top',top),('start',start),('bottom',bottom),('end',end)]:
        node=m.find(qn('w:'+k))
        if node is None: node=OxmlElement('w:'+k);m.append(node)
        node.set(qn('w:w'),str(v));node.set(qn('w:type'),'dxa')

def set_repeat_table_header(row):
    trPr=row._tr.get_or_add_trPr();el=OxmlElement('w:tblHeader');el.set(qn('w:val'),'true');trPr.append(el)

def set_table_geometry(table,widths):
    table.autofit=False;table.alignment=WD_TABLE_ALIGNMENT.LEFT
    tblPr=table._tbl.tblPr;tblW=tblPr.find(qn('w:tblW'));tblW.set(qn('w:w'),'9360');tblW.set(qn('w:type'),'dxa')
    ind=OxmlElement('w:tblInd');ind.set(qn('w:w'),'120');ind.set(qn('w:type'),'dxa');tblPr.append(ind)
    grid=table._tbl.tblGrid
    for child in list(grid): grid.remove(child)
    for w in widths:
        col=OxmlElement('w:gridCol');col.set(qn('w:w'),str(w));grid.append(col)
    for row in table.rows:
        for i,cell in enumerate(row.cells):
            cell.width=Inches(widths[i]/1440);cell.vertical_alignment=WD_CELL_VERTICAL_ALIGNMENT.CENTER;set_cell_margins(cell)
            tcW=cell._tc.get_or_add_tcPr().find(qn('w:tcW'));tcW.set(qn('w:w'),str(widths[i]));tcW.set(qn('w:type'),'dxa')

def set_run(run,size=None,bold=None,color=TEXT,name="Calibri"):
    run.font.name=name;run._element.get_or_add_rPr().rFonts.set(qn('w:ascii'),name);run._element.rPr.rFonts.set(qn('w:hAnsi'),name)
    if size: run.font.size=Pt(size)
    if bold is not None: run.bold=bold
    run.font.color.rgb=RGBColor.from_string(color)

def add_picture(doc,path,caption):
    p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.keep_with_next=True
    r=p.add_run();shape=r.add_picture(str(path),width=Inches(6.45));shape._inline.docPr.set('descr',caption)
    c=doc.add_paragraph();c.alignment=WD_ALIGN_PARAGRAPH.CENTER;c.paragraph_format.space_after=Pt(8)
    set_run(c.add_run(caption),9,False,MUTED)

def add_bullets(doc,items):
    for item in items:
        p=doc.add_paragraph(style='List Bullet');p.paragraph_format.space_after=Pt(4);p.paragraph_format.line_spacing=1.25
        set_run(p.add_run(item),11,False,TEXT)

doc=Document();sec=doc.sections[0];sec.top_margin=sec.bottom_margin=Inches(1);sec.left_margin=sec.right_margin=Inches(1);sec.page_width=Inches(8.5);sec.page_height=Inches(11)
doc.settings.odd_and_even_pages_header_footer=False
styles=doc.styles
normal=styles['Normal'];normal.font.name='Calibri';normal.font.size=Pt(11);normal.font.color.rgb=RGBColor.from_string(TEXT);normal.paragraph_format.space_after=Pt(6);normal.paragraph_format.line_spacing=1.25
for name,size,color,before,after in [('Title',30,NAVY,0,8),('Subtitle',14,MUTED,0,16),('Heading 1',16,TEAL,18,10),('Heading 2',13,TEAL,14,7),('Heading 3',12,'1F4D78',10,5)]:
    s=styles[name];s.font.name='Calibri';s.font.size=Pt(size);s.font.bold=name!='Subtitle';s.font.color.rgb=RGBColor.from_string(color);s.paragraph_format.space_before=Pt(before);s.paragraph_format.space_after=Pt(after);s.paragraph_format.keep_with_next=True
title_ppr=styles['Title']._element.get_or_add_pPr();title_border=title_ppr.find(qn('w:pBdr'))
if title_border is not None: title_ppr.remove(title_border)

# Running furniture
sec.header.paragraphs[0].text=""
footer=sec.footer.paragraphs[0];footer.alignment=WD_ALIGN_PARAGRAPH.CENTER
fld=OxmlElement('w:fldSimple');fld.set(qn('w:instr'),'PAGE');footer._p.append(fld)

# Editorial cover
p=doc.add_paragraph();p.paragraph_format.space_before=Pt(115);p.alignment=WD_ALIGN_PARAGRAPH.CENTER;set_run(p.add_run("TECHNICAL ARCHITECTURE"),11,True,AMBER)
p=doc.add_paragraph(style='Title');p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.add_run("LibraryForge")
pPr=p._p.get_or_add_pPr();pBdr=pPr.find(qn('w:pBdr'))
if pBdr is not None: pPr.remove(pBdr)
p=doc.add_paragraph(style='Subtitle');p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.add_run("Intelligent Library Management System")
p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.space_before=Pt(18);set_run(p.add_run("WPF  •  .NET 8  •  MVVM  •  EF Core 8  •  SQLite"),12,True,TEAL)
p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER;p.paragraph_format.space_before=Pt(95);set_run(p.add_run("Living architecture document"),12,True,TEXT)
p=doc.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER;set_run(p.add_run("Baseline 1.1  |  18 July 2026"),10,False,MUTED)
doc.add_page_break()

doc.add_heading('1. Purpose and architecture principles',1)
doc.add_paragraph('This document describes the implemented LibraryForge architecture and is intended to evolve alongside the application. It captures the current component boundaries, dependency direction, data relationships, critical workflows, and deliberate extension points.')
add_bullets(doc,["Offline-first: core circulation and local sign-in work without network services.","Service boundaries: business rules stay outside WPF code-behind.","Transactional consistency: copy counts and loan state change together.","Secure access: BCrypt local credentials and optional Google identity converge on RBAC-protected services.","Replaceable edges: identity providers, reporting, metadata lookup, recommendations, and sync can evolve independently."])
add_picture(doc,DIAGRAMS[0],'Figure 1. LibraryForge system context')

doc.add_heading('2. Solution structure and dependency direction',1)
add_picture(doc,DIAGRAMS[1],'Figure 2. Layered application architecture')
table=doc.add_table(rows=1,cols=3);table.style='Table Grid';hdr=table.rows[0].cells
for i,t in enumerate(['Layer','Current responsibility','Representative types']): hdr[i].text=t;set_cell_shading(hdr[i],TEAL);set_run(hdr[i].paragraphs[0].runs[0],10,True,WHITE)
rows=[('Views','Rendering, input, dialogs, accessibility','MainWindow, LoginWindow, RegistrationWindow'),('ViewModels','Observable state and commands','MainViewModel, MemberPortalViewModel'),('Services','Business workflows, identity, authorization','LoanService, AccountRegistrationService'),('Data / Domain','Persistence and entity rules','LibraryDbContext, Member, User, ExternalLogin')]
for row in rows:
    cells=table.add_row().cells
    for i,v in enumerate(row): cells[i].text=v;set_run(cells[i].paragraphs[0].runs[0],9.5,False,TEXT)
set_repeat_table_header(table.rows[0]);set_table_geometry(table,[1800,3600,3960])

doc.add_heading('3. Composition, startup, and navigation',1)
add_picture(doc,DIAGRAMS[2],'Figure 3. Startup and dependency injection flow')
doc.add_paragraph('App.xaml.cs owns the desktop lifecycle. Program.CreateHostBuilder composes configuration, Serilog, the pooled EF Core context factory, singleton services, transient view models, and transient views. The database initializer completes before the login window is shown. Successful authentication resolves and opens the main shell.')
doc.add_heading('Navigation model',2)
add_bullets(doc,["MainViewModel selects views through the service provider.","Viewer accounts open a personal My Library dashboard and read-only catalog; staff retain operational modules.","Admin navigation is only visible for administrators, and UserAdministrationService repeats the authorization check at the business boundary."])

doc.add_heading('4. Authentication and authorization',1)
add_picture(doc,DIAGRAMS[3],'Figure 4. Authentication and RBAC decision flow')
doc.add_paragraph('Passwords are never stored in plaintext. Local authentication verifies BCrypt hashes and applies lockout policy. Registration creates a linked Viewer user and Member record in an unverified state. Optional Google sign-in uses the system browser, Authorization Code with PKCE, a loopback callback, state and nonce checks, and validation of the signed OpenID Connect ID token before linking a provider subject to a local account. Provider tokens are not persisted.')
add_picture(doc,DIAGRAMS[8],'Figure 4B. Local email verification and session activation')
add_bullets(doc,["A six-digit code is generated with a cryptographic random-number generator; only its SHA-256 hash is stored.","Codes expire after ten minutes, resend is delayed for sixty seconds, and five incorrect attempts require a new code.","Local login is rejected until IsEmailVerified is true; successful verification creates the member session.","Default session duration is eight hours; the shell returns to login when it expires.","New and reset local passwords require at least 12 characters with upper/lowercase, a number, and a symbol.","Google-only accounts use Google's verified email claim and do not expose the local password-change action.","Navigation visibility improves usability, while AuthorizationService.Demand enforces the same permission at each business-service boundary."])
table=doc.add_table(rows=1,cols=3);table.style='Table Grid'
for i,t in enumerate(['Role','Permission profile','Protected operations']): table.rows[0].cells[i].text=t;set_cell_shading(table.rows[0].cells[i],TEAL);set_run(table.rows[0].cells[i].paragraphs[0].runs[0],10,True,WHITE)
for row in [('Viewer','My Library and catalog','Personal loans, reservations, history, read-only discovery'),('Librarian','Viewer access plus catalog, members, circulation, reservations','Book/member changes, issue/return, waitlists'),('Administrator','All permissions','Users, audit log, backup/restore, all staff workflows')]:
    c=table.add_row().cells
    for i,v in enumerate(row): c[i].text=v;set_run(c[i].paragraphs[0].runs[0],9.5,False,TEXT)
set_repeat_table_header(table.rows[0]);set_table_geometry(table,[1500,3900,3960])

doc.add_heading('5. Domain model and database',1)
add_picture(doc,DIAGRAMS[4],'Figure 5. Simplified entity relationship model')
doc.add_paragraph('LibraryDbContext exposes Books, BookCopies, InventoryEvents, Genres, Members, Loans, Reservations, Users, ExternalLogins, EmailVerifications, and AuditLogs. Each Book has many accessioned BookCopies; a Loan optionally references one exact copy so upgraded legacy loans remain readable. Users can optionally link one-to-one to a Member profile, one-to-many to external identity records, and one-to-zero-or-one pending verification record. Fluent configuration defines unique keys, lookup indexes, restricted deletes for circulation history, and inventory invariants.')
doc.add_heading('Performance-oriented indexes',2)
add_bullets(doc,["Book ISBN and composite title/author lookup.","Unique BookCopy accession code and optional barcode; book/status lookup for availability.","InventoryEvent copy/time lookup for condition and state history.","Member email and member code uniqueness.","Loan status/due-date and optional BookCopyId lookup.","Reservation book/status/queue-position lookup.","Unique user name, optional User.MemberId, and external provider subject."])

doc.add_heading('Physical-copy inventory and internal QR security',2)
add_picture(doc,DIAGRAMS[10],'Figure 5B. Accession inventory and installation-bound QR security boundary')
doc.add_paragraph('LibraryForge creates and validates QR content locally. QRCoder renders a standard QR image without contacting a hosted QR service. The encoded LF1 text is opaque AES-256-GCM ciphertext containing only the signed LibraryForge version, installation identifier, entity type, copy identifier, accession code, and issuance time. The encryption key is random per installation and protected with Windows Data Protection API for the current Windows user. A different installation, a modified token, or a mismatched database record is rejected before any action occurs.')
add_bullets(doc,["Standard scanners can extract the LF1 ciphertext text, but cannot recover or alter authenticated content without the installation key.","Permanent book labels record IssuedAtUtc for audit and key-rotation analysis; they do not expire during ordinary shelf life.","Future short-lived action QR tokens may add expiry and replay protection, but must use a separate payload purpose from permanent identity labels.","Database backup and QR-key backup are separate concerns: restoring the database under a different Windows identity requires an authorized key-transfer procedure that is not yet implemented."])

doc.add_heading('6. Circulation workflow',1)
add_picture(doc,DIAGRAMS[5],'Figure 6. Issue and return transaction flow')
doc.add_heading('Issue book',2)
add_bullets(doc,["Load the requested title, one Available BookCopy, and the member inside a database transaction.","Reject missing entities, unavailable physical copies, protected reservations, inactive members, and expired memberships.","Mark the exact copy Loaned, create the active loan with BookCopyId and due date, write its InventoryEvent, and recalculate title summaries.","Save the audit and inventory changes and commit as one unit."])
doc.add_heading('Return book',2)
add_bullets(doc,["Reject a missing or already returned loan.","Calculate overdue days against the configured daily fine rate.","Mark the loan returned and store the fine; return a healthy copy to Available or a damaged copy to Maintenance.","Write the InventoryEvent, recalculate title summaries, promote the reservation queue, and commit."])

doc.add_heading('7. Reservation lifecycle',1)
add_picture(doc,DIAGRAMS[6],'Figure 7. Reservation queue and promotion lifecycle')
doc.add_paragraph('ReservationService owns waitlist eligibility, FIFO queue positions, pickup expiry, cancellation, and promotion. Staff commands require ManageReservations. Member self-service commands derive MemberId from the active session and never accept an arbitrary member identifier. A title can be reserved only when all available copies are already protected or on loan.')
add_bullets(doc,["A member cannot hold a duplicate active reservation or reserve a title they already have on loan.","A member can cancel only a reservation whose MemberId matches the signed-in session; staff retain administrative cancellation.","Ready copies cannot be issued to another member when no unprotected copy remains.","Issuing to the reserved member marks the reservation fulfilled in the loan transaction.","Cancellation or pickup expiry reorders the queue and promotes the next eligible member.","Every state transition creates an AuditLog record in the same unit of work."])

doc.add_heading('8. Feature modules',1)
table=doc.add_table(rows=1,cols=4);table.style='Table Grid'
for i,t in enumerate(['Module','View / VM','Service','Current capability']): table.rows[0].cells[i].text=t;set_cell_shading(table.rows[0].cells[i],TEAL);set_run(table.rows[0].cells[i].paragraphs[0].runs[0],9.5,True,WHITE)
modules=[('Registration','RegistrationWindow / ViewModel','AccountRegistrationService, IEmailSender','Local registration, email confirmation, resend and Google linking'),('Settings','SettingsView / ViewModel','MemberPortalService, ThemeService, ProfileImageStorageService','Profile/photo editing, persistent appearance and password entry'),('My Library','MemberPortalView / ViewModel','MemberPortalService, ReservationService','Loans, reservations and owner-only cancellation'),('Browse','BrowseCatalogView / ViewModel','CatalogService, ReservationService','Viewer-safe search and self-service waitlist entry'),('Dashboard','DashboardView / ViewModel','DashboardService','Staff inventory, member and overdue statistics'),('Books','BooksView + editor/copies dialogs','CatalogService, InventoryService, SecureQrCodeService','CRUD, accession inventory, condition/status history, internal QR labels, import/export'),('Members','MembersView + MemberEditorWindow','MemberService','Validated membership editor and borrowing history'),('Circulation','Loans + Reservations views','LoanService, ReservationService','Exact-copy issue/return, fines, waitlist and promotion'),('Admin','AdminView / ViewModel','UserAdministrationService, AuditService, BackupService','Users, roles, audit and database maintenance')]
for row in modules:
    c=table.add_row().cells
    for i,v in enumerate(row): c[i].text=v;set_run(c[i].paragraphs[0].runs[0],8.8,False,TEXT)
set_repeat_table_header(table.rows[0]);set_table_geometry(table,[1400,2300,2400,3260])

doc.add_heading('Settings, profile images, and preferences',2)
add_picture(doc,DIAGRAMS[9],'Figure 8B. Settings and profile-media boundaries')
add_bullets(doc,["Profile edits derive MemberId from the authenticated session; verified email is read-only.","Profile images are restricted to PNG, JPEG, or BMP and five megabytes, then copied into the user LocalApplicationData LibraryForge directory.","Theme choice is applied through ThemeService and persisted per Windows account in preferences.json.","Book and member grids are read-only; dedicated editor dialogs validate detached copies before a service updates tracked entities.","CSV import requires Title and Author headers, handles quoted commas, skips invalid or duplicate-ISBN rows, and audits the batch."])

doc.add_heading('9. Cross-cutting operations',1)
add_picture(doc,DIAGRAMS[7],'Figure 8. Reporting and operational services')
add_bullets(doc,["ReportService generates inventory workbooks with ClosedXML and overdue PDFs with QuestPDF.","BackupService closes the EF database connection before copying or restoring the SQLite file.","AuditService records actor, action, entity, details, and UTC time; administrators can inspect recent events.","Serilog provides rolling diagnostic logs; MaterialDesign resources and PaletteHelper provide runtime light/dark switching."])

doc.add_heading('10. Implemented baseline and extension roadmap',1)
doc.add_paragraph('The following boundary is intentional: this document treats compiled code as the implemented baseline and records Phase 2 capabilities as extension points, not as completed features.')
table=doc.add_table(rows=1,cols=3);table.style='Table Grid'
for i,t in enumerate(['Status','Capability','Architectural landing zone']): table.rows[0].cells[i].text=t;set_cell_shading(table.rows[0].cells[i],TEAL);set_run(table.rows[0].cells[i].paragraphs[0].runs[0],10,True,WHITE)
roadmap=[('Implemented','Verified identity, Settings/profile images, self-service reservations, accession inventory, condition/loss history, installation-bound encrypted QR, exact-copy circulation, import/export, reports, RBAC, audit and themes','Existing services, views and database upgrade path'),('Next','Printable multi-label sheets, member-card QR and authorized QR-key recovery','Report templates and protected key-transfer workflow'),('Next','Configurable lending policies and reservation-ready notifications','Policy service and notification adapters'),('Future','Recommendations, fuzzy search, metadata lookup','Intelligence service interfaces; optional network adapters'),('Future','Kiosk camera decoding, barcode hardware, notifications and sync','Peripheral adapters and background hosted services')]
for status,cap,landing in roadmap:
    c=table.add_row().cells
    for i,v in enumerate((status,cap,landing)): c[i].text=v;set_run(c[i].paragraphs[0].runs[0],9.2,status=='Implemented' and i==0,GREEN if status=='Implemented' and i==0 else TEXT)
set_repeat_table_header(table.rows[0]);set_table_geometry(table,[1400,4100,3860])

doc.add_heading('11. Architecture update checklist',1)
add_bullets(doc,["Update the domain diagram when an entity, relationship, or index changes.","Update the relevant flow when a business rule or transaction boundary changes.","Add every new screen to the feature-module matrix and dependency diagram.","Document external APIs, hardware, or hosted services at the system-context boundary.","Rebuild and visually verify this DOCX after each meaningful architecture change."])

doc.core_properties.title='LibraryForge Technical Architecture'
doc.core_properties.subject='Living architecture reference for the .NET 8 WPF application'
doc.core_properties.author='LibraryForge Engineering'
doc.core_properties.keywords='WPF, .NET 8, MVVM, EF Core, SQLite, architecture'
doc.save(OUTPUT)
print(OUTPUT)
