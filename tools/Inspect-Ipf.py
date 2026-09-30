import pefile,capstone,re
from pathlib import Path
p=Path(r'C:\Windows\System32\DriverStore\FileRepository\ipf_cpu.inf_amd64_b1b7fa888ee1cf4a\ipfcore.dll')
pe=pefile.PE(str(p)); base=pe.OPTIONAL_HEADER.ImageBase
cs=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
for sym in pe.DIRECTORY_ENTRY_EXPORT.symbols:
 print(sym.name,hex(base+sym.address))
 if sym.name==b'GetIpfInterface':
  for i in cs.disasm(pe.get_data(sym.address,1400),base+sym.address):
   print(hex(i.address),i.mnemonic,i.op_str)
   if i.mnemonic=='ret': break
strings=[s.decode('ascii') for s in re.findall(rb'[ -~]{5,}',p.read_bytes())]
print('\nSTRINGS\n'+'\n'.join(s for s in strings if any(w in s.lower() for w in ['primitive','command','participant','fan','get_', 'sdk'])))
