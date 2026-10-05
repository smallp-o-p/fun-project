"""Extract static Grace shader inputs; run on source with --disable-autoexec.

Only output textures/manifest are written. View effects are disconnected during
EMIT baking and evaluated live by grace_crowne.gdshader. Geometry is not exported.
"""
import argparse, hashlib, json, sys
from pathlib import Path
import bpy
import numpy as np

def extract(output, channels=None):
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    source_hash = hashlib.sha256(Path(bpy.data.filepath).read_bytes()).hexdigest()
    objects = [bpy.data.objects[n] for n in ['grace_crowne', 'grace_crowne_hair', 'grace_crowne_suit', 'grace_crowne_suit_zipped', 'grace_crowne_goggles', 'grace_crowne_mask']]
    materials = {s.material.name:s.material for o in objects for s in o.material_slots}
    # Read exact selected source values, including linked Value/RGB/Switch nodes.
    def value(socket):
        if socket.is_linked:
            link=socket.links[0]
            if link.from_node.type=='GROUP' and link.from_node.node_tree.name=='FPv4 Switch':
                return value(link.from_node.inputs['Bool'])
            return link.from_socket.default_value
        return socket.default_value
    manifest={'version':1,'source_sha256':source_hash,'materials':{}}
    if channels is not None and (output/'materials.json').exists():
        previous=json.loads((output/'materials.json').read_text())
        assert previous['source_sha256']==source_hash, 'Partial extraction requires the same source'
        manifest=previous
    for name,mat in materials.items():
        row=manifest['materials'].get(name,{'textures':{}})
        for n in mat.node_tree.nodes:
            if n.type!='GROUP': continue
            if n.node_tree.name=='DD Skin':
                row['skin_exponent']=float(value(n.inputs['Skin Boost']))
                row['skin_ior']=1.45
            if n.node_tree.name=='FPv4 Hair' and n.name=='Group.001':
                row['hair_brightness']=float(value(n.inputs['BaseColor_Fresnel_Brightness']))
                row['hair_exponent']=float(value(n.inputs['Basecolor_Fresnel_Exponent']))
                row['hair_multiple']=float(value(n.inputs['Fresnel_Brightness_Multiple']))
                row['hair_emission']=float(value(n.inputs['Emissive_Brightness']))
                for socket,key in [('Hair Mask','hair_mask'),('AnisotropicTangentWeight','hair_tangent')]:
                    image=n.inputs[socket].links[0].from_node.image
                    image.filepath_raw=str(output/image.name);image.file_format='PNG';image.save()
                    row['textures'][key]=image.name
            if n.node_tree.name=='FPv4 MetalLUT' and n.name in ['Group.001','Group.002']:
                row['metal_lut_enabled']=float(value(n.inputs['Use MetalLUT']))
                row['metal_lut_intensity']=float(value(n.inputs['MetalLutIntensity']))
                row['metal_lut_mask']=list(value(n.inputs['MetalLUTMaskChannel']))[:3]
                row['metal_lut_channel']=list(value(n.inputs['LUTChannel']))[:3]
                row['metal_lut_curve']=float(value(n.inputs['Metal LUT Curve']))
            if n.node_tree.name=='FPv4 EmissiveFresnel' and n.name=='Group.003':
                row['emissive_fresnel_enabled']=float(value(n.inputs['Use Emissive Fresnel']))
                row['emissive_fresnel_power']=float(value(n.inputs['EmissiveFresnelPower']))
                row['emissive_fresnel_inverted']=float(value(n.inputs['InvertEmissiveFresnel']))
            if n.node_tree.name=='FPv4 Base Material' and n.name in ['Group.006','Group.003','Group.002','Group.005']:
                if n.inputs['FX Mask'].is_linked:
                    image=n.inputs['FX Mask'].links[0].from_node.image
                    image.filepath_raw=str(output/image.name);image.file_format='PNG';image.save()
                    row['textures']['fx_mask']=image.name
        manifest['materials'][name]=row
    atlas=bpy.data.images['FN_Character_Atlas.png'];atlas.filepath_raw=str(output/atlas.name);atlas.file_format='PNG';atlas.save()
    manifest['atlas']=atlas.name
    # Static bakes must never contain the camera-dependent DD/FPv4 terms.
    dd=bpy.data.node_groups['DD Skin']
    zero=dd.nodes.new('ShaderNodeValue');zero.outputs[0].default_value=0
    for link in list(dd.links):
        if link.from_node.name=='Group' and link.from_socket.name=='Value':
            target=link.to_socket;dd.links.remove(link);dd.links.new(zero.outputs[0],target)
    hair=bpy.data.node_groups['FPv4 Hair']
    hair_zero=hair.nodes.new('ShaderNodeValue');hair_zero.outputs[0].default_value=0
    hair_links=[]
    for link in list(hair.links):
        if link.from_node.name=='Group.009':
            hair_links.append(link.to_socket);hair.links.remove(link);hair.links.new(hair_zero.outputs[0],hair_links[-1])
    for mat in materials.values():
        for n in mat.node_tree.nodes:
            if n.type!='GROUP':continue
            if n.node_tree.name=='FPv4 MetalLUT':
                for l in list(n.inputs['Use MetalLUT'].links): mat.node_tree.links.remove(l)
                n.inputs['Use MetalLUT'].default_value=0
            if n.node_tree.name=='FPv4 EmissiveFresnel':
                for l in list(n.inputs['Use Emissive Fresnel'].links):mat.node_tree.links.remove(l)
                n.inputs['Use Emissive Fresnel'].default_value=False
            if n.node_tree.name=='FPv4 3L Eyes':n.inputs['Eye Refraction Mix'].default_value=0
    bpy.context.scene.render.engine='CYCLES'
    bpy.context.scene.cycles.samples=1
    bpy.context.scene.cycles.device='CPU'
    if bpy.context.object and bpy.context.object.mode!='OBJECT':bpy.ops.object.mode_set(mode='OBJECT')
    for obj in objects:
        obj.hide_set(False);obj.hide_viewport=False;obj.hide_render=False
        obj.animation_data_clear()
        for mod in obj.modifiers:
            mod.show_render=mod.show_viewport=mod.type in {'CORRECTIVE_SMOOTH','SUBSURF'}
    terminals=[]
    for tree in [m.node_tree for m in materials.values()]+list(bpy.data.node_groups):
        for node in list(tree.nodes):
            if node.type=='BSDF_PRINCIPLED':
                emit=tree.nodes.new('ShaderNodeEmission')
                outputs=[l.to_socket for l in list(node.outputs[0].links)]
                terminals.append((tree,node,emit,outputs))
    def connect(tree, node, socket, target):
        src=node.inputs.get(socket)
        if src is None:return
        if src.is_linked:tree.links.new(src.links[0].from_socket,target)
        else:
            v=src.default_value
            target.default_value=v if hasattr(v,'__len__') else (v,v,v,1) if target.type=='RGBA' else v
    def route(channel):
        for tree,node,emit,outputs in terminals:
            for l in list(emit.inputs[0].links):tree.links.remove(l)
            if channel in ('pbr','effects'):
                combine=tree.nodes.new('ShaderNodeCombineColor');combine.mode='RGB'
                sockets=['Metallic','Roughness','Specular IOR Level'] if channel=='pbr' else ['Subsurface Weight','Coat Weight','Coat Roughness']
                for index,socket in enumerate(sockets):
                    if channel=='effects' and index==0 and tree.name!='DD Skin':
                        combine.inputs[index].default_value=0
                    else:connect(tree,node,socket,combine.inputs[index])
                tree.links.new(combine.outputs[0],emit.inputs[0])
            else:connect(tree,node,'Base Color' if channel=='base' else 'Emission Color',emit.inputs[0])
            for target in outputs:tree.links.new(emit.outputs[0],target)
    channels=channels or ['normal','base','pbr','effects','emission','hair_emission']
    for channel in channels:
        if channel!='normal':route('emission' if channel=='hair_emission' else channel)
        if channel=='hair_emission':hair_zero.outputs[0].default_value=1
        targets={}
        for name,mat in materials.items():
            size=2048 if name=='grace_crowne_nude' else 1024
            image=bpy.data.images.new('GraceStatic_'+name+'_'+channel,width=size,height=size,alpha=False,float_buffer=channel in ['emission','hair_emission'])
            image.colorspace_settings.name='sRGB' if channel=='base' else 'Non-Color'
            image.generated_color=(.5,.5,1,1) if channel=='normal' else (0,0,0,1)
            target=mat.node_tree.nodes.new('ShaderNodeTexImage');target.image=image;mat.node_tree.nodes.active=target
            targets[name]=image
        # No shared material is cleared between objects; hair/goggles/mask UVs share an atlas.
        for obj in objects:
            bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
            assert bpy.ops.object.bake(type='NORMAL' if channel=='normal' else 'EMIT',normal_space='TANGENT',margin=8,use_clear=False)=={'FINISHED'}
        for name,image in targets.items():
            hdr=channel in ['emission','hair_emission']
            filename=name+'_'+channel+('.exr' if hdr else '.png');image.filepath_raw=str(output/filename);image.file_format='OPEN_EXR' if hdr else 'PNG';image.save()
            manifest['materials'][name]['textures'][channel]=filename
            pix=np.empty(len(image.pixels),dtype=np.float32);image.pixels.foreach_get(pix)
            manifest['materials'][name].setdefault('ranges',{})[channel]=[float(pix.reshape(-1,4)[:,:3].min()),float(pix.reshape(-1,4)[:,:3].max())]
        print('GRACE_STATIC_CHANNEL',channel,flush=True)
    (output/'materials.json').write_text(json.dumps(manifest,indent=2))
    print('GRACE_MATERIAL_EXTRACTION_COMPLETE',source_hash,flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--output',required=True);p.add_argument('--channels',nargs='+',choices=['normal','base','pbr','effects','emission','hair_emission'])
    args=p.parse_args(sys.argv[sys.argv.index('--')+1:]);extract(args.output,args.channels)
