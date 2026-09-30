import pefile,capstone,argparse
p=argparse.ArgumentParser();p.add_argument('binary');p.add_argument('addresses',nargs='+',type=lambda s:int(s,0));a=p.parse_args()
pe=pefile.PE(a.binary);base=pe.OPTIONAL_HEADER.ImageBase
imports={i.address:(d.dll.decode()+':'+(i.name.decode() if i.name else str(i.ordinal))) for d in pe.DIRECTORY_ENTRY_IMPORT for i in d.imports}
cs=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64);cs.detail=True
for address in a.addresses:
 f=next(e.struct for e in pe.DIRECTORY_ENTRY_EXCEPTION if e.struct.BeginAddress<=address-base<e.struct.EndAddress)
 print('\nFUNCTION',hex(base+f.BeginAddress),hex(base+f.EndAddress))
 for i in cs.disasm(pe.get_data(f.BeginAddress,f.EndAddress-f.BeginAddress),base+f.BeginAddress):
  note=[]
  for op in i.operands:
   if op.type==capstone.x86.X86_OP_MEM and op.mem.base==capstone.x86.X86_REG_RIP:
    target=i.address+i.size+op.mem.disp
    if target in imports:note.append(imports[target])
  print(hex(i.address),i.mnemonic,i.op_str,'; '+' '.join(note) if note else '')
