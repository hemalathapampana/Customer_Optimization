#!/usr/bin/env python3
"""
Cross-Provider Customer Optimization Flow Diagram Generator
Generates a JPG image of the color-coded data flow diagram.

Requirements:
pip install graphviz cairosvg pillow

Usage:
python generate_cross_provider_diagram.py
"""

from graphviz import Source
import cairosvg
from PIL import Image
import os

def generate_cross_provider_diagram():
    """Generate the Cross-Provider Customer Optimization Flow diagram as JPG"""
    
    # Graphviz code for the Cross-Provider Customer Optimization Flow
    graphviz_code = """
    digraph CrossProviderOptimization {
        rankdir=TB;
        node [shape=box, style=filled, fontname="Helvetica", fontsize=12];
        edge [fontname="Helvetica", fontsize=10];

        // Define nodes with colors matching the original design
        A [label="🚀 AMOP 2.0 Trigger\\nCross-Provider Customer Request", 
           fillcolor="#ff6b6b", color="#d63031", fontcolor="#ffffff", penwidth=3];
        
        B [label="🔐 Cross-Provider Session Management\\nMulti-Provider Customer Validation", 
           fillcolor="#4ecdc4", color="#00b894", fontcolor="#ffffff", penwidth=2];
        
        C [label="🔍 Cross-Provider Rate Plan Discovery\\nMulti-Provider Auto Change Detection", 
           fillcolor="#a8e6cf", color="#00b894", fontcolor="#000000", penwidth=2];
        
        D [label="✅ Cross-Provider Customer Validation\\nMulti-Provider Eligibility Check", 
           fillcolor="#ffd93d", color="#fdcb6e", fontcolor="#000000", penwidth=2];
        
        E [label="🏊 Cross-Provider Rate Pool Generation\\nMulti-Provider Customer Pool Calculation", 
           fillcolor="#d1a3ff", color="#a29bfe", fontcolor="#000000", penwidth=2];
        
        F [label="📋 Multi-Provider Queue Creation\\nCross-Provider Customer Job Queuing", 
           fillcolor="#74b9ff", color="#0984e3", fontcolor="#ffffff", penwidth=2];
        
        G [label="⚡ Multi-Provider Optimization Execution\\nCross-Provider Customer Algorithm Processing", 
           fillcolor="#fd79a8", color="#e84393", fontcolor="#ffffff", penwidth=2];
        
        H [label="📊 Cross-Provider Result Compilation\\nMulti-Provider Customer Data Aggregation", 
           fillcolor="#74b9ff", color="#0984e3", fontcolor="#ffffff", penwidth=2];
        
        I [label="📧 Cross-Provider Customer Email & Reporting\\nMulti-Provider Customer Finalization", 
           fillcolor="#d1a3ff", color="#a29bfe", fontcolor="#000000", penwidth=2];
        
        J [label="🧹 Cross-Provider Processing Cleanup\\nMulti-Provider Session Finalization", 
           fillcolor="#81ecec", color="#00cec9", fontcolor="#000000", penwidth=2];

        // Define edges with data flow labels
        A -> B [label="Customer Parameters\\nMulti-Provider Settings", fontsize=9];
        B -> C [label="Validated Customer Data\\nProvider Associations", fontsize=9];
        C -> D [label="Cross-Provider Rate Plan Status\\nProvider-Specific Capabilities", fontsize=9];
        D -> E [label="Validated Multi-Provider Customer Data\\nCross-Provider Eligibility Status", fontsize=9];
        E -> F [label="Cross-Provider Rate Pool Data\\nProvider-Specific Pool Configurations", fontsize=9];
        F -> G [label="Cross-Provider Queue Items\\nProvider-Specific Job Assignments", fontsize=9];
        G -> H [label="Cross-Provider Optimization Results\\nMulti-Provider Cost Analysis", fontsize=9];
        H -> I [label="Compiled Cross-Provider Results\\nConsolidated Multi-Provider Statistics", fontsize=9];
        I -> J [label="Cross-Provider Reports\\nMulti-Provider Optimization Summary", fontsize=9];

        // Add legend/title
        labelloc="t";
        label="Cross-Provider Customer Optimization System\\nData Flow Diagram";
        fontsize=16;
        fontname="Helvetica";
    }
    """

    try:
        print("🚀 Generating Cross-Provider Customer Optimization Flow Diagram...")
        
        # Define output paths
        base_path = "CrossProviderOptimizationFlow"
        svg_path = base_path + ".svg"
        png_path = base_path + ".png"
        jpg_path = base_path + ".jpg"
        
        # Step 1: Render SVG from Graphviz
        print("📊 Step 1: Rendering SVG from Graphviz...")
        Source(graphviz_code).render(base_path, format="svg", cleanup=True)
        print(f"✅ SVG generated: {svg_path}")
        
        # Step 2: Convert SVG to PNG
        print("🔄 Step 2: Converting SVG to PNG...")
        cairosvg.svg2png(url=svg_path, write_to=png_path, output_width=1200, output_height=1600)
        print(f"✅ PNG generated: {png_path}")
        
        # Step 3: Convert PNG to JPG
        print("🎨 Step 3: Converting PNG to JPG...")
        with Image.open(png_path) as img:
            # Convert to RGB if necessary (removes alpha channel)
            if img.mode != 'RGB':
                img = img.convert('RGB')
            
            # Save as high-quality JPG
            img.save(jpg_path, "JPEG", quality=95, optimize=True)
        print(f"✅ JPG generated: {jpg_path}")
        
        # Clean up intermediate files
        if os.path.exists(png_path):
            os.remove(png_path)
        if os.path.exists(svg_path):
            os.remove(svg_path)
            
        print(f"\n🎉 SUCCESS! Downloadable JPG created: {jpg_path}")
        print(f"📁 File size: {os.path.getsize(jpg_path) / 1024:.1f} KB")
        print(f"📍 Full path: {os.path.abspath(jpg_path)}")
        
        return jpg_path
        
    except ImportError as e:
        print(f"❌ Missing required library: {e}")
        print("Please install required libraries:")
        print("pip install graphviz cairosvg pillow")
        return None
        
    except Exception as e:
        print(f"❌ Error generating diagram: {e}")
        return None

if __name__ == "__main__":
    result = generate_cross_provider_diagram()
    if result:
        print(f"\n✨ Your downloadable JPG is ready: {result}")
        print("🔗 You can now use this JPG file in presentations, documents, or share it!")
    else:
        print("❌ Failed to generate diagram. Please check the error messages above.")