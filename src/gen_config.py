import re,glob,json,sys,os
MOD="T:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord/Modules"
names=set(open('meshnames.txt').read().split())
items={}
for f in glob.glob(MOD+'/SandBoxCore/ModuleData/items/*.xml')+glob.glob(MOD+'/NavalDLC/ModuleData/items/*.xml'):
    s=open(f,encoding='utf-8-sig').read()
    for m in re.finditer(r'<Item\b([^>]*?)(/>|>(.*?)</Item>)',s,re.S):
        a=m.group(1); body=m.group(3) or ''
        g=lambda k: (re.search(r'\b'+k+r'="([^"]*)"',a) or [None,None])[1]
        iid=g('id')
        if not iid: continue
        ar=re.search(r'<Armor\b([^>]*)/?>',body)
        vals={}
        if ar:
            for k,v in re.findall(r'(\w+)="([^"]*)"',ar.group(1)): vals[k]=v
        items[iid]=dict(id=iid,type=g('Type'),mesh=g('mesh'),culture=(g('culture') or '').replace('Culture.',''),name=g('name'),armor=vals)
def score(it):
    v=it['armor']; return sum(float(v.get(k,0)) for k in('head_armor','body_armor','leg_armor','arm_armor'))
def fem(mesh,gv):
    if gv:
        for suf in('_converted_slim','_slim','_converted',''):
            if mesh+suf in names: return mesh+suf
    return mesh if mesh in names else None
SLOTS=[('HeadArmor','head'),('BodyArmor','body'),('LegArmor','legs'),('HandArmor','hands'),('Cape','cape')]
BANNER={'empire':'empire_tier_3_banner','vlandia':'vlandia_tier_3_banner','sturgia':'sturgia_tier_3_banner','battania':'battania_tier_3_banner','khuzait':'khuzait_tier_3_banner','aserai':'banner_aserai_level3'}
SKIN=['body_female_a','head_female_a','hands_female_a','feet_female_a']
HAIR=['female_hair_z_c','female_eyebrow_2']
ANIM_BASE=['anim_female_walk_forward_unarmed','anim_female_run_forward_unarmed','anim_noble_standing_idle_female','anim_noble_standing_idle_female_2']
ANIM_BANNER=['pledge_troop_walk_with_banner_loop','anim_rider_walk_withbanner','anim_rider_canter_withbanner']
scenes=[]
scenes.append(dict(id='female_nude',title='Female - nude (base body, rigged)',category='Female',skeletal=True,parts=SKIN+HAIR,anims=ANIM_BASE))
setinfo={}
scenes.append(dict(id='female_nude_bare',title='Female - bare skin (underwear painted out)',category='Female',skeletal=True,parts=SKIN+HAIR,anims=ANIM_BASE,texmap={'body_female_a_d':'body_female_a_d_bare'},notes=['The game ships only one body texture, with underwear painted into it. This variant removes that paint (derived texture, 512px).']))
for cul in BANNER:
    chosen={}
    for typ,slot in SLOTS:
        c=[it for it in items.values() if it['type']==typ and it['culture']==cul and it['mesh'] and fem(it['mesh'],it['armor'].get('has_gender_variations')=='true')]
        if c:
            best=max(c,key=score); chosen[slot]=best
    parts=[];used=[]
    for slot in ('head','body','legs','hands','cape'):
        it=chosen.get(slot)
        if not it: continue
        m=fem(it['mesh'],it['armor'].get('has_gender_variations')=='true'); parts.append(m); used.append(f"{slot}: {it['id']} -> {m}")
    parts_skin=[]
    # always keep the face; hide body/hands/feet only when the armor slot exists
    parts_skin.append('head_female_a')
    parts_skin.append('body_female_a')
    if 'hands' not in chosen: parts_skin.append('hands_female_a')
    if 'legs' not in chosen: parts_skin.append('feet_female_a')
    hair=[] if 'head' in chosen else HAIR
    allparts=parts_skin+hair+parts
    scenes.append(dict(id=f'female_armored_{cul}',title=f'Female - fully armored ({cul})',category='Female',skeletal=True,parts=allparts,anims=ANIM_BASE,notes=used))
    scenes.append(dict(id=f'female_flagbearer_{cul}',title=f'Female - flag bearer ({cul})',category='Flag bearer',skeletal=True,parts=allparts,
        static=[dict(mesh=BANNER[cul],attach='r_hand')],anims=ANIM_BANNER+ANIM_BASE[:1],notes=used))
    setinfo[cul]=used
# standalone banners / flags
for cul,mn in BANNER.items():
    for t in (1,2,3):
        mm=mn.replace('tier_3',f'tier_{t}').replace('level3',f'level{t}')
        if mm in names: scenes.append(dict(id=f'banner_{cul}_t{t}',title=f'Banner - {cul} tier {t}',category='Banners & flags',skeletal=False,parts=[mm]))
for mm in ['flagpole_a','flagpole_b','campaign_flag','flagbearer_a','flagbearer_b','generic_flag_pole','cloth_flag_a','encounter_flag_a','generic_flag_vertical_a','generic_flag_horizontal_a']:
    if mm in names: scenes.append(dict(id=f'prop_{mm}',title=f'Flag prop - {mm}',category='Banners & flags',skeletal=False,parts=[mm]))
json.dump(dict(scenes=scenes),open('config.json','w'),indent=1)
for k,v in setinfo.items(): print(k,v)
print(len(scenes),'scenes')
