import openpyxl
from collections import Counter

p = r'D:\AI\vibe\ZHCC\out\test.xlsx'
wb = openpyxl.load_workbook(p)
ws = wb.active
print('sheet', ws.title, ws.dimensions, 'freeze', ws.freeze_panes, 'filter', ws.auto_filter.ref)
for row in ws.iter_rows(min_row=1, max_row=4):
    print(' | '.join(f'{c.coordinate}={c.value!r}<{c.data_type},{c.number_format}>' for c in row))
print('font', ws['A1'].font.name, ws['A1'].font.sz, ws['A1'].font.bold, ws['A1'].alignment.horizontal)
print('border?', ws['A1'].border.left.style)

# 类型统计
types = Counter()
for row in ws.iter_rows(min_row=2, max_row=ws.max_row):
    for c in row:
        types[(c.column_letter, c.data_type, c.number_format)] += 1
for k, v in sorted(types.items()):
    print(k, v)
