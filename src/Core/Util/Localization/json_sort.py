import json
from pathlib import Path
import sys

def get_script_dir():
    """使用 pathlib 获取脚本所在目录"""
    return Path(__file__).parent.absolute()

def sort_json_file(input_file, output_file=None):
    """对JSON文件中的键进行排序"""
    # 获取脚本所在目录
    script_dir = get_script_dir()
    
    # 构建完整的输入文件路径
    input_path = script_dir / input_file

    # 读取JSON文件
    with open(input_file, 'r', encoding='utf-8') as f:
        data = json.load(f)
    
    # 递归排序函数
    def sort_keys(obj):
        if isinstance(obj, dict):
            return {k: sort_keys(v) for k, v in sorted(obj.items())}
        elif isinstance(obj, list):
            return [sort_keys(item) for item in obj]
        return obj
    
    # 排序
    sorted_data = sort_keys(data)
    
    # 确定输出文件路径
    if output_file is None:
        output_path = input_path
    else:
        output_path = script_dir / output_file

    # 保存~
    output = output_file or input_file
    with open(output, 'w', encoding='utf-8') as f:
        json.dump(sorted_data, f, ensure_ascii=False, indent=2)
    
    print(f"已保存到: {output}")

# 使用示例
if __name__ == "__main__":
    
    sort_json_file("texts_zh.json")
    sort_json_file("texts_en.json")