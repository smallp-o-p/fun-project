"""Bind extracted static PBR channels in the clean Blender authoring file.

Runtime camera effects are supplied by the Godot importer, never folded here.
"""
import json
from pathlib import Path
import bpy

def apply(material_directory):
    folder=Path(material_directory)
    manifest=json.loads((folder/'materials.json').read_text())
    for name,row in manifest['materials'].items():
        material=bpy.data.materials[name]
        material.animation_data_clear();material.node_tree.animation_data_clear()
        tree=material.node_tree;tree.nodes.clear()
        output=tree.nodes.new('ShaderNodeOutputMaterial');shader=tree.nodes.new('ShaderNodeBsdfPrincipled')
        tree.links.new(shader.outputs['BSDF'],output.inputs['Surface'])
        shader.inputs['Subsurface Scale'].default_value=.005
        shader.inputs['Subsurface Radius'].default_value=(1,.2,.1)
        shader.inputs['IOR'].default_value=row.get('skin_ior',1.5)
        for channel,socket in [('base','Base Color'),('normal','Normal'),('emission','Emission Color'),('pbr',None),('effects',None)]:
            image=bpy.data.images.load(str(folder/row['textures'][channel]),check_existing=True)
            image.colorspace_settings.name='sRGB' if channel=='base' else 'Non-Color'
            image.pack()
            texture=tree.nodes.new('ShaderNodeTexImage');texture.image=image
            if channel=='normal':
                normal=tree.nodes.new('ShaderNodeNormalMap');tree.links.new(texture.outputs['Color'],normal.inputs['Color']);tree.links.new(normal.outputs['Normal'],shader.inputs[socket])
            elif socket:tree.links.new(texture.outputs['Color'],shader.inputs[socket])
            else:
                split=tree.nodes.new('ShaderNodeSeparateColor');tree.links.new(texture.outputs['Color'],split.inputs['Color'])
                names=['Metallic','Roughness','Specular IOR Level'] if channel=='pbr' else ['Subsurface Weight','Coat Weight','Coat Roughness']
                for color,target in zip(['Red','Green','Blue'],names):tree.links.new(split.outputs[color],shader.inputs[target])
        shader.inputs['Emission Strength'].default_value=1
        material.use_backface_culling=False
    return manifest
