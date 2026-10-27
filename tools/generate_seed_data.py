"""
Generates synthetic student account data for the Student Account Workbench.

Everything here is made up: institutions, students, vendor and amounts. The goal is data
that behaves like a real student AR ledger so the reports have something realistic to show:
Title IV disbursements and credit balance refunds, VA certifications that are still waiting on
payment, military TA, employer payments, payment plans run by a third-party vendor, late payers,
withdrawals with Return of Title IV adjustments, and so on.

Proportions are modelled on Covista's FY2026 public results (year ended June 30, 2026), scaled
from ~99,500 students to 2,500: revenue mix ~38% nursing (Chamberlain-like), ~41% online
(Walden-like), ~20% medical and veterinary; revenue per student ~$19k nursing, ~$15k online,
~$78k med/vet; total ~$49M a year.

Output:
    db/05_seed_data.sql
    samples/clearpath_settlement_2026-10-01_2026-10-09.csv   (vendor file with planted exceptions)
    src/Workbench.Web/wwwroot/samples/...                    (same file, served by the web app)

Run:  python3 tools/generate_seed_data.py
Deterministic (fixed random seed), so re-running gives the same data.
"""

import csv
import random
from dataclasses import dataclass, field
from datetime import date, timedelta
from pathlib import Path

random.seed(20261026)

ROOT = Path(__file__).resolve().parent.parent
SEED_SQL = ROOT / "db" / "05_seed_data.sql"
VENDOR_CSV = ROOT / "samples" / "clearpath_settlement_2026-10-01_2026-10-09.csv"
VENDOR_CSV_WEB_COPY = ROOT / "src" / "Workbench.Web" / "wwwroot" / "samples" / VENDOR_CSV.name  # served by the web app

DATA_CUTOFF = date(2026, 10, 27)  # nothing gets posted after this date
STUDENT_COUNT = 3550

# id, code, name, segment
INSTITUTIONS = [
    (1, "LCN", "Lakeshore College of Nursing", "Nursing"),
    (2, "MHU", "Meridian Health University", "Online"),
    (3, "CSM", "Coral Sound School of Medicine", "Medical and Veterinary"),
    (4, "WSM", "Windward University School of Medicine", "Medical and Veterinary"),
    (5, "CVM", "Coral Sound School of Veterinary Medicine", "Medical and Veterinary"),
]

# id, institution, code, name, level, per-credit rate (None = flat), flat tuition, credit range, weight, online
# Weights give ~40% nursing / ~55% online / ~5% med-vet students, matching Covista's enrollment mix.
PROGRAMS = [
    (101, 1, "BSN",   "Bachelor of Science in Nursing",            "Undergraduate", 750, None, (12, 15), 178, False),
    (102, 1, "RNBSN", "RN to BSN (Online)",                        "Undergraduate", 590, None, (6, 9), 139, True),
    (103, 1, "MSN",   "Master of Science in Nursing (Online)",     "Graduate",      690, None, (6, 9), 80, True),
    (201, 2, "MSNO",  "MS in Nursing",                             "Graduate",      765, None, (6, 9), 165, True),
    (202, 2, "MPH",   "Master of Public Health",                   "Graduate",      600, None, (6, 9), 110, True),
    (203, 2, "MSW",   "Master of Social Work",                     "Graduate",      615, None, (6, 12), 110, True),
    (204, 2, "DNP",   "Doctor of Nursing Practice",                "Doctoral",      850, None, (6, 9), 80, True),
    (205, 2, "BSHS",  "BS in Health Studies",                      "Undergraduate", 355, None, (9, 15), 86, True),
    (301, 3, "MD",    "Doctor of Medicine",                        "Doctoral",      None, 25500, None, 20, False),
    (401, 4, "MD",    "Doctor of Medicine",                        "Doctoral",      None, 25000, None, 15, False),
    (501, 5, "DVM",   "Doctor of Veterinary Medicine",             "Doctoral",      None, 23500, None, 16, False),
]

# code, description, start, end, payment due, fee due
TERMS = [
    ("202520", "Summer 2025", date(2025, 5, 11), date(2025, 8, 15),  date(2025, 5, 25), date(2025, 5, 2)),
    ("202530", "Fall 2025",   date(2025, 8, 31), date(2025, 12, 19), date(2025, 9, 14), date(2025, 8, 15)),
    ("202610", "Spring 2026", date(2026, 1, 11), date(2026, 5, 1),   date(2026, 1, 25), date(2026, 1, 2)),
    ("202620", "Summer 2026", date(2026, 5, 10), date(2026, 8, 14),  date(2026, 5, 24), date(2026, 5, 1)),
    ("202630", "Fall 2026",   date(2026, 8, 30), date(2026, 12, 18), date(2026, 9, 13), date(2026, 8, 14)),
]

# Calibration knob for every tuition rate. At 1.0, ~2,500 enrolled students produce ~$49M a year,
# i.e. Covista's ~$19.6k of revenue per enrolled student.
TUITION_SCALE = 1.0

# online programs run two 8-week sessions per term; session 2 is billed and due on its own schedule
SESSION2_BILLED_DAY = 42
SESSION2_DUE_DAY = 70

DETAIL_CODES = [
    ("TUIT", "Tuition",                                "CHG", "Institution"),
    ("FEES", "Technology and Student Services Fee",    "CHG", "Institution"),
    ("CLIN", "Clinical Placement Fee",                 "CHG", "Institution"),
    ("MATL", "Course Materials and Lab Kits",          "CHG", "Institution"),
    ("PELL", "Federal Pell Grant",                     "AID", "TitleIV"),
    ("DLSU", "Direct Subsidized Loan",                 "AID", "TitleIV"),
    ("DLUN", "Direct Unsubsidized Loan",               "AID", "TitleIV"),
    ("GPLS", "Direct Grad PLUS Loan",                  "AID", "TitleIV"),
    ("VA33", "VA Post-9/11 GI Bill (Ch. 33)",          "AID", "VA"),
    ("VA31", "VA Veteran Readiness and Employment (Ch. 31)", "AID", "VA"),
    ("MLTA", "Military Tuition Assistance",            "AID", "MilitaryTA"),
    ("SCHL", "Institutional Scholarship",              "AID", "Scholarship"),
    ("EMPL", "Employer Tuition Payment",               "PAY", "Employer"),
    ("CARD", "Student Payment - Card/ACH",             "PAY", "SelfPay"),
    ("PPLN", "Payment Plan Payment - ClearPath",       "PAY", "PaymentPlan"),
    ("RFND", "Credit Balance Refund to Student",       "ADJ", "TitleIV"),
    ("WDTA", "Withdrawal Tuition Adjustment",          "ADJ", "Institution"),
    ("RT4R", "Return of Title IV Funds",               "ADJ", "TitleIV"),
    ("WOFF", "Bad Debt Write-off (sent to collections)", "ADJ", "Institution"),
]

FIRST_NAMES = """Aaliyah Abdul Adriana Aisha Alejandro Amara Andre Angela Anthony Ariana Ashley Ayesha Bianca
Brandon Brianna Camila Carlos Chen Chloe Christopher Daniela Darnell David Deja Diego Elena Emily Emmanuel
Fatima Gabriel Grace Hannah Hector Imani Isabella Jamal Jasmine Javier Jennifer Jessica Jose Joshua Kayla
Keisha Kevin Kimberly Lauren Leah Luis Malik Maria Marcus Maya Megan Michael Mohammed Nadia Natalie Nia
Nicole Olivia Omar Patricia Priya Rachel Rafael Rebecca Ricardo Rosa Samantha Sara Sofia Stephanie Tamika
Tiffany Tyler Valeria Vanessa Victoria Wei Xavier Yesenia Zainab""".split()

LAST_NAMES = """Adams Ahmed Alvarez Anderson Baker Banks Bell Brooks Brown Bryant Campbell Carter Castillo Chen
Clark Coleman Collins Cruz Davis Diaz Edwards Evans Flores Foster Garcia Gomez Gonzalez Gray Green Hall
Harris Hernandez Hill Howard Hughes Jackson James Jenkins Johnson Jones Kelly Khan Kim Lee Lewis Lopez
Martin Martinez Mitchell Moore Morales Morgan Murphy Nguyen O'Brien Ortiz Parker Patel Perez Phillips
Price Ramirez Reed Reyes Rivera Roberts Robinson Rodriguez Ross Sanchez Scott Shah Simmons Smith Stewart
Taylor Thomas Thompson Torres Turner Walker Washington Watson White Williams Wilson Wright Young""".split()


@dataclass
class Txn:
    student_id: str
    term: str
    code: str
    amount: float
    effective: date
    due: date | None = None
    ref: str | None = None


@dataclass
class Student:
    student_id: str
    first: str
    last: str
    email: str
    institution_id: int
    program: tuple
    military: str | None
    va_chapter: str | None = None
    va_pct: float = 1.0
    status: str = "Active"
    terms: list = field(default_factory=list)


txns: list[Txn] = []
va_certs = []        # (student, term, chapter, certified, expected, status, paid)
plans = []           # (student, term, vendor_plan_id, amount, installments, enrolled, status)
installments = []    # (vendor_plan_id, installment_no, due_date, amount)
ref_counter = [100000]


def money(x: float) -> float:
    return round(x + 1e-9, 2)


def add(t: Txn):
    if t.effective <= DATA_CUTOFF and abs(t.amount) >= 0.01:
        t.amount = money(t.amount)
        txns.append(t)


def next_ref(prefix: str) -> str:
    ref_counter[0] += random.randint(1, 7)
    return f"{prefix}-{ref_counter[0]}"


def pick_weighted(items, weight_index):
    return random.choices(items, weights=[i[weight_index] for i in items])[0]


def make_students() -> list[Student]:
    students = []
    used_ids = set()
    for n in range(STUDENT_COUNT):
        while True:
            sid = "S" + str(random.randint(20000000, 29999999))
            if sid not in used_ids:
                used_ids.add(sid)
                break
        first = random.choice(FIRST_NAMES)
        last = random.choice(LAST_NAMES)
        email = f"{first}.{last}{n % 97}@student.example.edu".lower().replace("'", "")
        program = pick_weighted(PROGRAMS, 8)

        r = random.random()
        military = "Veteran" if r < 0.08 else "ActiveDuty" if r < 0.12 else "Dependent" if r < 0.14 else None
        s = Student(sid, first, last, email, program[1], program, military)
        if military in ("Veteran", "Dependent"):
            s.va_chapter = "31" if military == "Veteran" and random.random() < 0.15 else "33"
            s.va_pct = 1.0 if s.va_chapter == "31" else random.choice([1.0, 1.0, 1.0, 0.9, 0.8, 0.7, 0.6])
        students.append(s)
    return students


def charges_for(s: Student, term) -> tuple[float, float, float, int]:
    """returns tuition, fees, clinical fee, credits"""
    _, _, code, _, level, rate, flat, credit_range, _, _ = s.program
    if flat:
        tuition, credits = money(flat * TUITION_SCALE), 15
    else:
        credits = random.randint(*credit_range)
        tuition = money(rate * TUITION_SCALE) * credits
    fees = 1450 if flat else 395 if level == "Undergraduate" else 285
    clinical = 350 if code in ("BSN", "MSN", "MSNO", "DNP") else 0
    return tuition, fees, clinical, credits


def simulate_term(s: Student, term, is_first_term: bool):
    code, _, start, end, due, fee_due = term
    sid = s.student_id
    level = s.program[4]
    is_flat = s.program[6] is not None
    online = s.program[9]
    tuition, fees, clinical, credits = charges_for(s, term)
    full_time = credits >= 12 if level == "Undergraduate" else credits >= 6

    # online programs: half the tuition is session 1 (billed with the term), half is session 2
    session1 = money(tuition / 2) if online else tuition
    session2 = tuition - session1
    s2_billed = start + timedelta(days=SESSION2_BILLED_DAY)
    s2_due = start + timedelta(days=SESSION2_DUE_DAY)

    # most students register ahead of time; late registrants get billed with a due date 14 days out
    if random.random() < 0.06:
        registered = start + timedelta(days=random.randint(5, 30))
        add(Txn(sid, code, "FEES", fees, registered, registered + timedelta(days=14)))
        add(Txn(sid, code, "TUIT", session1, registered, registered + timedelta(days=14)))
    else:
        add(Txn(sid, code, "FEES", fees, fee_due - timedelta(days=21), fee_due))
        add(Txn(sid, code, "TUIT", session1, start - timedelta(days=21), due))
    if session2:
        add(Txn(sid, code, "TUIT", session2, s2_billed, s2_due))
    clinical_posted = start + timedelta(days=28)
    if clinical:
        add(Txn(sid, code, "CLIN", clinical, clinical_posted, clinical_posted + timedelta(days=21)))
    materials = 0 if is_flat else random.choice([95, 140, 185, 220])
    materials_posted = start + timedelta(days=36)
    add(Txn(sid, code, "MATL", materials, materials_posted, materials_posted + timedelta(days=14)))
    charges = tuition + fees + clinical + materials

    # withdrawal during the term?
    term_days = (end - start).days
    withdraw_date = None
    if random.random() < 0.05:
        withdraw_date = start + timedelta(days=int(term_days * random.uniform(0.08, 0.55)))
        if withdraw_date > DATA_CUTOFF:
            withdraw_date = None

    expected_credits = 0.0
    titleiv = []
    va_pending = False

    # --- VA ---
    if s.va_chapter:
        certified = start - timedelta(days=random.randint(5, 20))
        cap = 14960.0
        expected = (tuition + fees) if s.va_chapter == "31" else min(tuition + fees, cap) * s.va_pct
        # VA is slower at fall peak, so a chunk of Fall 2026 is still waiting
        paid_on = certified + timedelta(days=random.randint(35, 85) if code == "202630" else random.randint(24, 62))
        # a few certifications stuck at VA well past the 90-day protection window
        if code == "202620" and random.random() < 0.07:
            paid_on = date(2027, 1, 15)
        status = "PAID" if paid_on <= DATA_CUTOFF else "PENDING"
        va_pending = status == "PENDING"
        va_certs.append((sid, code, s.va_chapter, certified, money(expected), status, paid_on if status == "PAID" else None))
        add(Txn(sid, code, "VA" + s.va_chapter, -expected, paid_on))
        expected_credits += expected

    # --- Military TA ---
    if s.military == "ActiveDuty":
        ta = min(250 * credits, 2250, tuition)
        add(Txn(sid, code, "MLTA", -ta, start + timedelta(days=random.randint(20, 40))))
        expected_credits += ta

    # --- Title IV ---
    gets_titleiv = s.military != "ActiveDuty" and random.random() < (0.9 if is_flat else 0.3 if s.va_chapter else 0.72)
    if gets_titleiv:
        if level == "Undergraduate":
            if random.random() < 0.45:
                titleiv.append(("PELL", 3697.5 if full_time else 1849.0))
            if not s.va_chapter:
                titleiv.append(("DLSU", 1750.0 if full_time else 875.0))
                titleiv.append(("DLUN", 1000.0 if full_time else 500.0))
        else:
            titleiv.append(("DLUN", 10250.0))
            remaining = charges + (12000 if is_flat else 2500) - 10250
            # Grad PLUS closed to new borrowers from July 1, 2026, so Fall 2026 first-timers don't get it
            if is_flat and remaining > 0 and not (code == "202630" and is_first_term):
                titleiv.append(("GPLS", remaining))

    disb_date = start + timedelta(days=10)
    aid_held = code == "202630" and random.random() < 0.05  # verification / SAP hold, not disbursed yet
    titleiv_total = 0.0
    if titleiv and not aid_held:
        for aid_code, amt in titleiv:
            add(Txn(sid, code, aid_code, -amt, disb_date))
            titleiv_total += amt
        expected_credits += titleiv_total

    # --- scholarship ---
    if random.random() < 0.1:
        sch = random.choice([500, 750, 1000, 1500, 2000])
        add(Txn(sid, code, "SCHL", -sch, start - timedelta(days=5)))
        expected_credits += sch

    # --- employer ---
    if s.program[2] == "RNBSN" and random.random() < 0.25:
        emp = min(2625.0, max(0, charges - expected_credits))
        if emp > 0:
            add(Txn(sid, code, "EMPL", -emp, start + timedelta(days=random.randint(35, 50))))
            expected_credits += emp

    # credit balance refund within 14 days of the disbursement that created it
    paid_so_far = expected_credits
    if titleiv_total and paid_so_far > charges:
        refund = paid_so_far - charges
        add(Txn(sid, code, "RFND", refund, disb_date + timedelta(days=random.randint(5, 12))))
        paid_so_far = charges

    # --- withdrawal adjustments ---
    if withdraw_date:
        pct_done = (withdraw_date - start).days / term_days
        credit_pct = 0.6 if pct_done < 0.2 else 0.3 if pct_done < 0.4 else 0.0
        if credit_pct:
            add(Txn(sid, code, "WDTA", -tuition * credit_pct, withdraw_date + timedelta(days=3)))
            charges -= tuition * credit_pct
        if titleiv_total and pct_done <= 0.6:
            returned = min(titleiv_total * (1 - pct_done), charges * (1 - pct_done))
            posted = withdraw_date + timedelta(days=random.randint(20, 40))
            add(Txn(sid, code, "RT4R", returned, posted, posted + timedelta(days=30)))
            charges += returned
        s.status = "Withdrawn"

    # --- student's own share ---
    owed = money(charges - paid_so_far)
    if owed <= 0:
        return 0.0

    if withdraw_date:
        if random.random() < 0.3:
            add(Txn(sid, code, "CARD", -owed * random.choice([1, 0.5]), withdraw_date + timedelta(days=random.randint(40, 90))))
        return 0.0

    weights = [58, 30, 7, 5]
    if va_pending and s.va_pct < 1:
        weights = [40, 10, 25, 25]  # partial-eligibility students often hold off paying their share until VA pays
    behavior = random.choices(["card", "plan", "late", "delinquent"], weights=weights)[0]
    if behavior == "card" and online:
        # pays each session's share by that session's due date
        part2 = min(owed, session2)
        part1 = owed - part2
        if part1 > 0:
            add(Txn(sid, code, "CARD", -part1, due - timedelta(days=random.randint(-2, 12))))
        add(Txn(sid, code, "CARD", -part2, s2_due - timedelta(days=random.randint(-2, 10))))
    elif behavior == "card":
        if random.random() < 0.3:
            add(Txn(sid, code, "CARD", -owed / 2, due - timedelta(days=random.randint(1, 10))))
            add(Txn(sid, code, "CARD", -(owed - money(owed / 2)), due + timedelta(days=random.randint(-3, 20))))
        else:
            add(Txn(sid, code, "CARD", -owed, due - timedelta(days=random.randint(-2, 12))))
    elif behavior == "late":
        add(Txn(sid, code, "CARD", -owed, due + timedelta(days=random.randint(20, 110))))
    elif behavior == "delinquent":
        portion = random.choice([0, 0, 0.25, 0.5])
        if portion:
            add(Txn(sid, code, "CARD", -owed * portion, due + timedelta(days=random.randint(5, 40))))
        return money(owed * (1 - portion))
    else:
        plan_count = 4
        each = money(owed / plan_count)
        plan_id = f"CPP-{300000 + len(plans) * 7 + random.randint(1, 6)}"
        enrolled = start - timedelta(days=random.randint(5, 25))
        defaults_after = random.choice([None] * 9 + [2])  # one in ten stops paying after 2 installments
        made = 0
        installments_rows = []
        for k in range(plan_count):
            month = start.month + k + (1 if start.day > 20 else 0)
            year = start.year + (month - 1) // 12
            month = (month - 1) % 12 + 1
            inst_due = date(year, month, 5)
            inst_amt = each if k < plan_count - 1 else money(owed - each * (plan_count - 1))
            installments_rows.append((plan_id, k + 1, inst_due, inst_amt))
            pay_day = inst_due + timedelta(days=random.randint(-2, 3))
            if defaults_after is not None and k >= defaults_after:
                continue  # stopped paying; the schedule still says this was due
            if pay_day > DATA_CUTOFF:
                continue
            add(Txn(sid, code, "PPLN", -inst_amt, pay_day))
            made += 1
        missed_due = [r[2] for r in installments_rows[defaults_after:]] if defaults_after is not None else []
        if missed_due and missed_due[0] <= DATA_CUTOFF - timedelta(days=10):
            status = "CANCELLED"  # vendor cancels about 10 days after a missed installment
        elif made == plan_count:
            status = "COMPLETED"
        else:
            status = "ACTIVE"
        plans.append((sid, code, plan_id, owed, plan_count, enrolled, status))
        installments.extend(installments_rows)
        if status == "CANCELLED":
            return money(owed - each * made)
    return 0.0


def simulate(students):
    # roughly steady enrolment growing ~9% a year, like Covista's
    first_term_weights = [58, 10, 11, 7, 14]
    for s in students:
        first = random.choices(range(len(TERMS)), weights=first_term_weights)[0]
        carry, carry_term = 0.0, None
        for idx in range(first, len(TERMS)):
            term = TERMS[idx]
            if idx > first:
                if s.status == "Withdrawn":
                    break
                if carry > 0:
                    # a registration hold: pay the old balance before the term starts, or don't come back
                    if random.random() < 0.6:
                        add(Txn(s.student_id, carry_term, "CARD", -carry, term[2] - timedelta(days=random.randint(3, 14))))
                    else:
                        s.status = "Inactive"
                        break
                if random.random() < 0.06:
                    s.status = "Inactive"
                    break
                if term[0] in ("202520", "202620") and s.program[2] == "BSN" and random.random() < 0.35:
                    continue  # campus BSN students sometimes skip summer; online programs run year-round
            s.terms.append(term[0])
            carry, carry_term = simulate_term(s, term, idx == first), term[0]


def write_off_old_balances():
    """Balances still unpaid ~9 months after the term ends go to a collection agency and come off the books."""
    term_end = {t[0]: t[3] for t in TERMS}
    balances = {}
    for t in txns:
        balances[(t.student_id, t.term)] = balances.get((t.student_id, t.term), 0) + t.amount
    for (sid, term), bal in balances.items():
        when = term_end[term] + timedelta(days=270)
        if bal > 1 and when <= DATA_CUTOFF:
            add(Txn(sid, term, "WOFF", -bal, when))


def assign_vendor_refs():
    # vendor transaction ids go up over time, like a real processor's
    for t in sorted((t for t in txns if t.code == "PPLN"), key=lambda t: (t.effective, t.student_id)):
        t.ref = next_ref("CP")


def plant_vendor_file_exceptions(students):
    """
    Builds the ClearPath settlement file for 10/01-10/09 from what was actually paid,
    then breaks a few things on purpose so the reconciliation page has real exceptions to find.
    """
    window_start, window_end = date(2026, 10, 1), date(2026, 10, 9)
    in_window = [t for t in txns if t.code == "PPLN" and window_start <= t.effective <= window_end]
    random.shuffle(in_window)

    file_rows = [
        {"vendor_txn_id": t.ref, "student_id": t.student_id, "payment_date": t.effective.isoformat(),
         "amount": f"{-t.amount:.2f}", "payment_method": random.choice(["ACH", "ACH", "CARD"]),
         "plan_id": next(p[2] for p in plans if p[0] == t.student_id and p[1] == t.term)}
        for t in in_window
    ]
    by_ref = {r["vendor_txn_id"]: r for r in file_rows}

    # 1) three payments the vendor collected that never got posted to the student ledger
    for t in in_window[0:3]:
        txns.remove(t)
    # 2) two amounts keyed wrong when posted
    in_window[3].amount = money(in_window[3].amount / 10)
    in_window[4].amount = money(in_window[4].amount + 50)
    # 3) one payment posted to the wrong student
    other = next(t for t in in_window[10:] if t.student_id != in_window[5].student_id)
    in_window[5].student_id = other.student_id
    # 4) one ledger payment the vendor never settled (reversed on their side)
    file_rows.remove(by_ref[in_window[6].ref])

    # 5) the vendor sent one row twice
    file_rows.append(dict(by_ref[in_window[7].ref]))
    # 6) a student id that doesn't exist in our system
    file_rows.append({"vendor_txn_id": next_ref("CP"), "student_id": "S2O114857", "payment_date": "2026-10-06",
                      "amount": "312.50", "payment_method": "ACH", "plan_id": "CPP-318204"})
    # 7) a row with a missing amount
    file_rows.append({"vendor_txn_id": next_ref("CP"), "student_id": in_window[8].student_id,
                      "payment_date": "2026-10-07", "amount": "", "payment_method": "CARD",
                      "plan_id": by_ref[in_window[8].ref]["plan_id"]})

    file_rows.sort(key=lambda r: (r["payment_date"], r["vendor_txn_id"]))
    VENDOR_CSV.parent.mkdir(exist_ok=True)
    with VENDOR_CSV.open("w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=["vendor_txn_id", "student_id", "payment_date", "amount",
                                          "payment_method", "plan_id"])
        w.writeheader()
        w.writerows(file_rows)
    VENDOR_CSV_WEB_COPY.parent.mkdir(parents=True, exist_ok=True)
    VENDOR_CSV_WEB_COPY.write_bytes(VENDOR_CSV.read_bytes())
    return len(file_rows)


def sql_str(v):
    if v is None:
        return "NULL"
    if isinstance(v, date):
        return f"'{v.isoformat()}'"
    if isinstance(v, (int, float)):
        return f"{v:.2f}" if isinstance(v, float) else str(v)
    return "N'" + str(v).replace("'", "''") + "'"


def insert_block(table, columns, rows, batch=900):
    out = []
    for i in range(0, len(rows), batch):
        chunk = rows[i:i + batch]
        values = ",\n".join("(" + ", ".join(sql_str(v) for v in r) + ")" for r in chunk)
        out.append(f"INSERT INTO {table} ({', '.join(columns)}) VALUES\n{values};")
    return "\n".join(out) + "\n"


def write_sql(students):
    parts = ["-- Generated by tools/generate_seed_data.py. Synthetic data only. Do not edit by hand.",
             "SET NOCOUNT ON;", ""]
    parts.append(insert_block("dbo.Institution", ["InstitutionId", "Code", "Name", "Segment"], INSTITUTIONS))
    parts.append(insert_block("dbo.Program", ["ProgramId", "InstitutionId", "Code", "Name", "AwardLevel"],
                              [p[:5] for p in PROGRAMS]))
    parts.append(insert_block("dbo.Term", ["TermCode", "Description", "StartDate", "EndDate", "PaymentDueDate"],
                              [t[:5] for t in TERMS]))
    parts.append(insert_block("dbo.DetailCode", ["DetailCode", "Description", "Category", "FundSource"], DETAIL_CODES))
    parts.append(insert_block(
        "dbo.Student",
        ["StudentId", "FirstName", "LastName", "Email", "InstitutionId", "ProgramId", "EnrollmentStatus", "MilitaryAffiliation"],
        [(s.student_id, s.first, s.last, s.email, s.institution_id, s.program[0],
          s.status if s.terms and s.terms[-1] == "202630" or s.status != "Active" else "Inactive", s.military)
         for s in students]))
    parts.append(insert_block(
        "dbo.VaCertification",
        ["StudentId", "TermCode", "Chapter", "CertifiedDate", "ExpectedAmount", "Status", "PaidDate"], va_certs))
    parts.append(insert_block(
        "dbo.PaymentPlan",
        ["StudentId", "TermCode", "VendorPlanId", "PlanAmount", "Installments", "EnrolledDate", "Status"], plans))
    parts.append(insert_block(
        "dbo.PaymentPlanInstallment", ["VendorPlanId", "InstallmentNo", "DueDate", "Amount"], installments))
    txns.sort(key=lambda t: (t.effective, t.student_id))
    parts.append(insert_block(
        "dbo.AccountTransaction",
        ["StudentId", "TermCode", "DetailCode", "Amount", "EffectiveDate", "DueDate", "ExternalRef", "PostedBy"],
        [(t.student_id, t.term, t.code, t.amount, t.effective, t.due, t.ref,
          "CLEARPATH_FEED" if t.code == "PPLN" else "SYSTEM") for t in txns]))
    SEED_SQL.write_text("\n".join(parts))


def print_calibration(students):
    """Quick check of the totals against the Covista-scaled targets (revenue recognised evenly over each term)."""
    fy_start, fy_end = date(2025, 7, 1), date(2026, 6, 30)
    term_dates = {t[0]: (t[2], t[3]) for t in TERMS}
    seg_of = {i[0]: i[3] for i in INSTITUTIONS}
    inst_of = {s.student_id: s.institution_id for s in students}
    revenue = {}
    for t in txns:
        if t.code in ("TUIT", "FEES", "CLIN", "MATL", "WDTA", "SCHL"):
            start, end = term_dates[t.term]
            overlap = (min(end, fy_end) - max(start, fy_start)).days + 1
            if overlap > 0:
                seg = seg_of[inst_of[t.student_id]]
                revenue[seg] = revenue.get(seg, 0) + t.amount * overlap / ((end - start).days + 1)
    total = sum(revenue.values())
    print(f"FY2026 net revenue ${total/1e6:.1f}M (target ~$49M)")
    for seg, amt in sorted(revenue.items()):
        print(f"  {seg:<24} ${amt/1e6:5.1f}M  {amt/total:5.1%}")
    ytd = {}
    for label, (a, b) in {"FY2026 YTD": (date(2025, 7, 1), date(2025, 10, 27)),
                          "FY2027 YTD": (date(2026, 7, 1), date(2026, 10, 27))}.items():
        ytd[label] = sum(t.amount * ((min(term_dates[t.term][1], b) - max(term_dates[t.term][0], a)).days + 1)
                         / ((term_dates[t.term][1] - term_dates[t.term][0]).days + 1)
                         for t in txns if t.code in ("TUIT", "FEES", "CLIN", "MATL", "WDTA", "SCHL")
                         and term_dates[t.term][0] <= b and term_dates[t.term][1] >= a)
    print(f"YTD growth: {ytd['FY2027 YTD'] / ytd['FY2026 YTD'] - 1:+.1%} (Covista ~+9%)")
    fall = [s for s in students if "202630" in s.terms]
    print(f"Fall 2026 students: {len(fall)}")


def main():
    students = make_students()
    simulate(students)
    write_off_old_balances()
    assign_vendor_refs()
    file_rows = plant_vendor_file_exceptions(students)
    write_sql(students)
    print_calibration(students)
    print(f"students={len(students)} transactions={len(txns)} va_certs={len(va_certs)} "
          f"plans={len(plans)} vendor_file_rows={file_rows}")


if __name__ == "__main__":
    main()
