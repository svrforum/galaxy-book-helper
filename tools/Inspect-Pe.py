"""Read-only PE disassembly and string cross-references; never loads the binary."""
import argparse,pefile,capstone
p=argparse.ArgumentParser();p.add_argument('binary');p.add_argument('--address',type=lambda x:int(x,0));p.add_argument('--size',type=lambda x:int(x,0),default=512);p.add_argument('--xref');a=p.parse_args()
pe=pefile.PE(a.binary);base=pe.OPTIONAL_HEADER.ImageBase;cs=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64);cs.detail=True;cs.skipdata=True
if a.address:
 for i in cs.disasm(pe.get_data(a.address-base,a.size),a.address): print(hex(i.address),i.mnemonic,i.op_str)
if a.xref:
 data=pe.get_memory_mapped_image();needle=a.xref.encode()+b'\0';pos=0;targets=[]
 while (pos:=data.find(needle,pos))>=0: targets.append(base+pos);pos+=1
 print('Strings:',[hex(t) for t in targets])
 for section in pe.sections:
  if not section.Characteristics & 0x20000000:continue
  for i in cs.disasm(section.get_data(),base+section.VirtualAddress):
   for op in (i.operands if i.id else []):
    if op.type==capstone.x86.X86_OP_MEM and op.mem.base==capstone.x86.X86_REG_RIP and i.address+i.size+op.mem.disp in targets:print(hex(i.address),i.mnemonic,i.op_str)
